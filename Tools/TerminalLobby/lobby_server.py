#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""EmpireCraft 终端模式的联机大厅(自己架服务器用的那一版；只用标准库，python3 lobby_server.py 就能跑)。

一个端口同时做三件事：
  · 网页入口：GET / 是大厅页(同目录的 lobby.html)，GET /api/rooms 是房间列表；
  · 房主接入：游戏用 WebSocket 连 /host?room=房间号&key=钥匙，发 HELLO / UPDATE 报房间信息；
  · 中转：手机访问 /r/房间号/…，原样转给房主那条连接(文本帧 REQ)，等它回(二进制帧 RES)再交还手机。
世界在房主的电脑上算，这里不存任何游戏数据；只记"房间号 → 钥匙"(lobby_keys.json)，免得别人拿着房间号顶替房主。
协议与游戏里的 Scripts/Terminal/TerminalRelay.cs 对应；Cloudflare 上的 worker.js 是同一套协议的另一种实现。
"""
import argparse
import asyncio
import base64
import gzip
import hashlib
import json
import os
import re
import struct
import time

ROOM = re.compile(r'^[0-9a-f]{16}$')
KEY = re.compile(r'^[0-9a-f]{32}$')
TOKEN = re.compile(r'^[0-9a-f]{0,64}$')
RELAY = re.compile(r'^/r/([0-9a-f]{16})(/.*)?$')
GUID = b'258EAFA5-E914-47DA-95CA-C5AB0DC85B11'

MAX_HEAD, MAX_BODY, MAX_FRAME = 8192, 4096, 1024 * 1024
IDLE_SECONDS, HOST_SILENT_SECONDS, REPLY_SECONDS = 20, 90, 8
MAX_ROOMS, MAX_ROOMS_PER_IP, MAX_NAME = 500, 3, 24
RATE_PER_SECOND, RATE_BURST = 30.0, 90.0
KEY_KEEP_SECONDS = 60 * 86400
# 房主那边不常变的几个文件：每个房间各存一份，省得每个来人都穿一趟
STATIC = {'/manifest.json', '/icon-192.png', '/icon-512.png', '/apple-touch-icon.png', '/FactionFrame.png', '/FactionFrame_dominate.png'}
COMPRESSIBLE = ('text/', 'application/json', 'application/manifest+json', 'application/javascript')

rooms = {}
keys = {}
buckets = {}
HERE = os.path.dirname(os.path.abspath(__file__))
state = {'page': None, 'page_mtime': 0, 'keys_path': None, 'keys_dirty': False}


class Room:
    def __init__(self, room_id, address, writer):
        self.id, self.ip, self.writer = room_id, address, writer
        self.info = None
        self.pending = {}
        self.seq = 0
        self.cache = {}
        self.lock = asyncio.Lock()
        self.since = time.time()


def log(*parts):
    print(time.strftime('%Y-%m-%d %H:%M:%S'), *parts, flush=True)


def json_bytes(value):
    return json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode('utf-8')


def error(status, code):
    return status, 'application/json; charset=utf-8', json_bytes({'error': code}), {}


def allowed(address):
    """每个地址每秒至多 RATE_PER_SECOND 个请求(攒得下 RATE_BURST 个)。"""
    now = time.monotonic()
    tokens, at = buckets.get(address, (RATE_BURST, now))
    tokens = min(RATE_BURST, tokens + (now - at) * RATE_PER_SECOND)
    if tokens < 1.0:
        buckets[address] = (tokens, now)
        return False
    buckets[address] = (tokens - 1.0, now)
    if len(buckets) > 20000:
        buckets.clear()
    return True


def lobby_page():
    path = os.path.join(HERE, 'lobby.html')
    try:
        mtime = os.path.getmtime(path)
        if state['page'] is None or mtime != state['page_mtime']:
            with open(path, 'rb') as handle:
                state['page'] = handle.read()
            state['page_mtime'] = mtime
    except OSError:
        return None
    return state['page']


# ───────── 钥匙 ─────────
def load_keys(path):
    state['keys_path'] = path
    try:
        with open(path, 'r', encoding='utf-8') as handle:
            for room_id, entry in json.load(handle).items():
                if ROOM.match(room_id) and KEY.match(str(entry.get('key', ''))):
                    keys[room_id] = {'key': entry['key'], 'at': float(entry.get('at', 0))}
    except (OSError, ValueError):
        pass


def save_keys():
    if not state['keys_dirty'] or not state['keys_path']:
        return
    state['keys_dirty'] = False
    cutoff = time.time() - KEY_KEEP_SECONDS
    for room_id in [room_id for room_id, entry in keys.items() if entry['at'] < cutoff and room_id not in rooms]:
        del keys[room_id]
    temporary = state['keys_path'] + '.tmp'
    try:
        with open(temporary, 'w', encoding='utf-8') as handle:
            json.dump(keys, handle)
        os.replace(temporary, state['keys_path'])
    except OSError as problem:
        log('keys not saved:', problem)


async def keys_loop():
    while True:
        await asyncio.sleep(30)
        save_keys()


# ───────── HTTP ─────────
async def read_request(reader, timeout):
    try:
        head = await asyncio.wait_for(reader.readuntil(b'\r\n\r\n'), timeout)
    except (asyncio.IncompleteReadError, asyncio.LimitOverrunError, asyncio.TimeoutError):
        return None
    if len(head) > MAX_HEAD:
        return None
    lines = head.decode('latin-1').split('\r\n')
    first = lines[0].split(' ')
    if len(first) < 2:
        return None
    headers = {}
    for line in lines[1:]:
        name, colon, value = line.partition(':')
        if colon:
            headers[name.strip().lower()] = value.strip()
    try:
        length = int(headers.get('content-length', '0') or '0')
    except ValueError:
        return None
    if length < 0 or length > MAX_BODY:
        return None
    body = await asyncio.wait_for(reader.readexactly(length), timeout) if length else b''
    return first[0].upper(), first[1], headers, body


STATUS = {200: 'OK', 302: 'Found', 400: 'Bad Request', 401: 'Unauthorized', 403: 'Forbidden', 404: 'Not Found', 405: 'Method Not Allowed',
          426: 'Upgrade Required', 429: 'Too Many Requests', 500: 'Error', 503: 'Service Unavailable', 504: 'Gateway Timeout'}


async def write_response(writer, status, content_type, body, request_headers, keep, extra):
    headers = {'Content-Type': content_type, 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff'}
    headers.update(extra or {})
    if len(body) > 1024 and 'gzip' in request_headers.get('accept-encoding', '') and content_type.startswith(COMPRESSIBLE):
        body = gzip.compress(body, 5)
        headers['Content-Encoding'] = 'gzip'
        headers['Vary'] = 'Accept-Encoding'
    headers['Content-Length'] = str(len(body))
    headers['Connection'] = 'keep-alive' if keep else 'close'
    head = 'HTTP/1.1 %d %s\r\n' % (status, STATUS.get(status, 'Error')) + ''.join('%s: %s\r\n' % pair for pair in headers.items()) + '\r\n'
    writer.write(head.encode('latin-1') + body)
    await writer.drain()


def room_list():
    listed = [room for room in rooms.values() if room.info]
    listed.sort(key=lambda room: room.since, reverse=True)
    return [{'id': room.id, 'name': room.info['name'], 'open': room.info['open'], 'used': room.info['used'], 'max': room.info['max'],
             'ver': room.info['ver']} for room in listed[:200]]


async def route(method, path, query, headers, body, address):
    if method == 'GET' and path in ('/', '/index.html'):
        page = lobby_page()
        return (200, 'text/html; charset=utf-8', page, {}) if page else error(404, 'no_page')
    if method == 'GET' and path == '/api/rooms':
        return 200, 'application/json; charset=utf-8', json_bytes({'rooms': room_list()}), {}
    if method == 'GET' and path == '/api/join':
        match = re.search(r'(?:^|&)room=([0-9a-f]{16})(?:&|$)', query)
        room = rooms.get(match.group(1)) if match else None
        if not room or not room.info:
            return error(404, 'offline')
        return 200, 'application/json; charset=utf-8', json_bytes({'open': room.info['open'], 'code': room.info['code'] if room.info['open'] else ''}), {}
    if path == '/healthz':
        return 200, 'text/plain', b'ok', {}
    match = RELAY.match(path)
    if not match:
        return error(404, 'not_found')
    if not match.group(2):
        return 302, 'text/plain', b'', {'Location': path + '/' + ('?' + query if query else '')}
    if method not in ('GET', 'POST'):
        return error(405, 'method')
    room = rooms.get(match.group(1))
    if not room:
        return error(503, 'host_offline')
    token = headers.get('x-terminal-token', '')
    return await relay(room, method, match.group(2), token if TOKEN.match(token) else '', body)


async def relay(room, method, path, token, body):
    path = path.replace('\r', '').replace('\n', '')[:200]
    cacheable = method == 'GET' and path in STATIC
    if cacheable and path in room.cache:
        status, content_type, payload = room.cache[path]
        return status, content_type, payload, {'Cache-Control': 'public, max-age=3600'}
    room.seq += 1
    request_id = str(room.seq)
    future = asyncio.get_running_loop().create_future()
    room.pending[request_id] = future
    try:
        text = '\n'.join(('REQ', request_id, method, path, token, body.decode('utf-8', 'replace')))
        await send_frame(room, 0x1, text.encode('utf-8'))
        status, content_type, payload = await asyncio.wait_for(future, REPLY_SECONDS)
    except asyncio.TimeoutError:
        return error(504, 'host_timeout')
    except (ConnectionError, OSError):
        return error(503, 'host_offline')
    finally:
        room.pending.pop(request_id, None)
    if cacheable and status == 200:
        room.cache[path] = (status, content_type, payload)
        return status, content_type, payload, {'Cache-Control': 'public, max-age=3600'}
    return status, content_type, payload, {}


# ───────── WebSocket(房主) ─────────
async def send_frame(room, opcode, payload):
    length = len(payload)
    if length < 126:
        head = struct.pack('!BB', 0x80 | opcode, length)
    elif length <= 0xFFFF:
        head = struct.pack('!BBH', 0x80 | opcode, 126, length)
    else:
        head = struct.pack('!BBQ', 0x80 | opcode, 127, length)
    async with room.lock:
        room.writer.write(head + payload)
        await room.writer.drain()


def unmask(data, mask):
    count = len(data)
    if not count:
        return data
    repeated = (mask * (count // 4 + 1))[:count]
    return (int.from_bytes(data, 'big') ^ int.from_bytes(repeated, 'big')).to_bytes(count, 'big')


async def read_frame(reader):
    first, second = await asyncio.wait_for(reader.readexactly(2), HOST_SILENT_SECONDS)
    length = second & 0x7F
    if length == 126:
        length = struct.unpack('!H', await reader.readexactly(2))[0]
    elif length == 127:
        length = struct.unpack('!Q', await reader.readexactly(8))[0]
    if length > MAX_FRAME:
        raise ValueError('frame too long')
    mask = await reader.readexactly(4) if second & 0x80 else None
    payload = await reader.readexactly(length) if length else b''
    return bool(first & 0x80), first & 0x0F, unmask(payload, mask) if mask else payload


def parse_info(text):
    fields = {}
    for line in text.split('\n'):
        name, equals, value = line.partition('=')
        if equals:
            fields[name.strip()] = value

    def number(name, low, high):
        try:
            return max(low, min(high, int(fields.get(name, '0'))))
        except ValueError:
            return low

    name = ''.join(ch for ch in fields.get('name', '') if ch >= ' ' and ch not in '<>\x7f').strip()[:MAX_NAME] or 'EmpireCraft'
    is_open = fields.get('open') == '1'
    code = fields.get('code', '')
    return {'name': name, 'open': is_open, 'used': number('used', 0, 64), 'max': number('max', 1, 64),
            'code': code if is_open and re.match(r'^\d{6}$', code) else '', 'ver': number('ver', 0, 100000)}


async def host_session(reader, writer, headers, query, address):
    def refuse(status, reason):
        writer.write(('HTTP/1.1 %d %s\r\nContent-Length: 0\r\nConnection: close\r\n\r\n' % (status, reason)).encode('latin-1'))

    params = dict(part.partition('=')[::2] for part in query.split('&') if part)
    room_id, key = params.get('room', ''), params.get('key', '')
    socket_key = headers.get('sec-websocket-key', '')
    if headers.get('upgrade', '').lower() != 'websocket' or not socket_key:
        return refuse(426, 'upgrade')
    if not ROOM.match(room_id) or not KEY.match(key):
        return refuse(400, 'bad_room')
    saved = keys.get(room_id)
    if saved and saved['key'] != key:
        return refuse(403, 'key')
    if room_id not in rooms:
        if len(rooms) >= MAX_ROOMS:
            return refuse(503, 'lobby_full')
        if sum(1 for room in rooms.values() if room.ip == address) >= MAX_ROOMS_PER_IP:
            return refuse(429, 'too_many')
    keys[room_id] = {'key': key, 'at': time.time()}
    state['keys_dirty'] = True
    accept = base64.b64encode(hashlib.sha1(socket_key.encode('latin-1') + GUID).digest()).decode('latin-1')
    writer.write(('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: %s\r\n\r\n' % accept)
                 .encode('latin-1'))
    await writer.drain()

    previous = rooms.get(room_id)
    room = Room(room_id, address, writer)
    rooms[room_id] = room
    if previous:
        # 同一位房主重连：旧的那条连接作废
        try:
            await send_frame(previous, 0x8, struct.pack('!H', 4000) + b'replaced')
            previous.writer.close()
        except (ConnectionError, OSError):
            pass
    log('room up  ', room_id, address)
    message, kind = b'', 0
    try:
        while True:
            fin, opcode, payload = await read_frame(reader)
            if opcode == 0x8:
                break
            if opcode == 0x9:
                await send_frame(room, 0xA, payload)
                continue
            if opcode == 0xA:
                continue
            if opcode in (0x1, 0x2):
                kind, message = opcode, payload
            elif opcode == 0x0:
                message += payload
                if len(message) > MAX_FRAME:
                    break
            else:
                continue
            if not fin:
                continue
            if kind == 0x1:
                head, _, rest = message.decode('utf-8', 'replace').partition('\n')
                if head in ('HELLO', 'UPDATE'):
                    room.info = parse_info(rest)
            elif kind == 0x2:
                line, newline, body = message.partition(b'\n')
                parts = line.decode('latin-1').split(' ', 3)
                if newline and len(parts) >= 3 and parts[0] == 'RES' and parts[2].isdigit():
                    future = room.pending.get(parts[1])
                    if future and not future.done():
                        future.set_result((int(parts[2]), parts[3] if len(parts) > 3 else 'text/plain', body))
            message = b''
    except (asyncio.IncompleteReadError, asyncio.TimeoutError, ConnectionError, OSError, ValueError):
        pass
    finally:
        if rooms.get(room_id) is room:
            del rooms[room_id]
            keys[room_id]['at'] = time.time()
            state['keys_dirty'] = True
            log('room down', room_id)
        for future in room.pending.values():
            if not future.done():
                future.set_exception(ConnectionError('host gone'))


async def handle(reader, writer):
    peer = writer.get_extra_info('peername')
    address = peer[0] if peer else '?'
    try:
        while True:
            request = await read_request(reader, IDLE_SECONDS)
            if request is None:
                break
            method, target, headers, body = request
            path, _, query = target.partition('?')
            if path == '/host':
                await host_session(reader, writer, headers, query, address)
                break
            if not allowed(address):
                status, content_type, payload, extra = error(429, 'slow_down')
            else:
                try:
                    status, content_type, payload, extra = await route(method, path, query, headers, body, address)
                except Exception as problem:  # 一个请求出错不拖垮服务
                    log('request failed:', repr(problem))
                    status, content_type, payload, extra = error(500, 'failed')
            keep = headers.get('connection', '').lower() != 'close'
            await write_response(writer, status, content_type, payload, headers, keep, extra)
            if not keep:
                break
    except (ConnectionError, OSError, asyncio.TimeoutError, asyncio.IncompleteReadError):
        pass
    finally:
        try:
            writer.close()
        except Exception:
            pass


async def main():
    parser = argparse.ArgumentParser(description='EmpireCraft terminal lobby')
    parser.add_argument('--host', default='0.0.0.0')
    parser.add_argument('--port', type=int, default=8765)
    parser.add_argument('--data', default=HERE, help='folder for lobby_keys.json')
    options = parser.parse_args()
    load_keys(os.path.join(options.data, 'lobby_keys.json'))
    server = await asyncio.start_server(handle, options.host, options.port, limit=MAX_HEAD)
    log('lobby listening on %s:%d' % (options.host, options.port))
    saver = asyncio.ensure_future(keys_loop())
    try:
        async with server:
            await server.serve_forever()
    finally:
        saver.cancel()
        state['keys_dirty'] = True
        save_keys()


if __name__ == '__main__':
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
