"""Run the local wardrobe alignment editor."""
import argparse
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import re
from tempfile import NamedTemporaryFile
from threading import Lock
from urllib.parse import urlsplit
import webbrowser

from terminal_portraits import (
    ALIGNMENT, BEGIN, END, GARMENT_NECK, GARMENT_SOLE, MAX_PAGE_BYTES, PAGE,
    garment_soles, portrait_payload, read_alignment, validate_alignment,
)

HERE = Path(__file__).resolve().parent
PAGE = Path(PAGE)
ALIGNMENT = Path(ALIGNMENT)
RENDER_BEGIN, RENDER_END = '/*PORTRAIT-RENDER-BEGIN*/', '/*PORTRAIT-RENDER-END*/'
SAVE_LOCK = Lock()


def page_parts():
    text = PAGE.read_bytes().decode('utf-8')
    before, block = text.split(BEGIN, 1)
    encoded, after = block.split(END, 1)
    payload = encoded.strip()
    if not payload.startswith('var PORTRAIT=') or not payload.endswith(';'):
        raise ValueError('终端网页的立绘数据无法读取')
    return before, json.loads(payload[len('var PORTRAIT='):-1]), after


def revision(data):
    return hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest()


def atomic_write(path, data):
    with NamedTemporaryFile(dir=path.parent, delete=False) as staging:
        temporary = Path(staging.name)
        try:
            staging.write(data)
        except Exception:
            staging.close()
            temporary.unlink(missing_ok=True)
            raise
    try:
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


class Handler(BaseHTTPRequestHandler):
    def reply(self, status, mime, data):
        self.send_response(status)
        self.send_header('Content-Type', mime)
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(data)

    def reply_json(self, status, value):
        self.reply(status, 'application/json; charset=utf-8',
                   json.dumps(value, ensure_ascii=False).encode())

    def do_GET(self):
        url = urlsplit(self.path)
        try:
            if url.path == '/':
                self.reply(200, 'text/html; charset=utf-8', (HERE / 'portrait_alignment.html').read_bytes())
            elif url.path == '/editor.js':
                self.reply(200, 'text/javascript; charset=utf-8', (HERE / 'portrait_alignment.js').read_bytes())
            elif url.path == '/templates.json':
                self.reply(200, 'application/json; charset=utf-8', (HERE / 'portrait_templates' / 'index.json').read_bytes())
            elif re.fullmatch(r'/templates/[a-z]+_[mf]\.png', url.path):
                self.reply(200, 'image/png', (HERE / 'portrait_templates' / url.path.split('/')[-1]).read_bytes())
            elif url.path == '/runtime.js':
                before, parts, after = page_parts()
                if not parts.get('hd'):
                    raise ValueError('请先打包终端立绘')
                source = (before + after).split(RENDER_BEGIN, 1)[1].split(RENDER_END, 1)[0]
                parts['alignment'] = read_alignment(parts)
                settings = dict(revision=revision(parts['alignment']), neck=GARMENT_NECK, sole=GARMENT_SOLE)
                script = 'var BASE="";' + portrait_payload(parts) + source + '\nvar FIT=' + json.dumps(settings) + ';'
                self.reply(200, 'text/javascript; charset=utf-8', script.encode())
            elif re.fullmatch(r'/portraits-hd-\d{2}-[0-9a-f]{12}\.webp', url.path):
                self.reply(200, 'image/webp', (PAGE.parent / url.path[1:]).read_bytes())
            else:
                self.reply_json(404, {'error': '文件不存在'})
        except FileNotFoundError:
            self.reply_json(404, {'error': '文件不存在'})
        except (OSError, ValueError, IndexError) as error:
            self.reply_json(500, {'error': str(error)})

    def do_POST(self):
        if self.path not in ('/save', '/soles'):
            self.reply_json(404, {'error': '无效的操作'})
            return
        origin = f'http://127.0.0.1:{self.server.server_port}'
        if self.headers.get('Origin') != origin or self.headers.get_content_type() != 'application/json':
            self.reply_json(403, {'error': '请从本地对位编辑器操作'})
            return
        try:
            length = int(self.headers.get('Content-Length', '0'))
            if not 0 < length <= MAX_PAGE_BYTES:
                raise ValueError('无效的请求长度')
            request = json.loads(self.rfile.read(length))
            if not isinstance(request, dict):
                raise ValueError('无效的请求')
            with SAVE_LOCK:
                before, parts, after = page_parts()
                if self.path == '/soles':
                    outfit = request.get('outfit')
                    if not isinstance(outfit, str) or not outfit.startswith(('body:', 'base:')) or outfit not in parts['tiles']:
                        raise ValueError('衣身图层不存在')
                    self.reply_json(200, {'soles': sorted(garment_soles(parts, outfit))})
                    return
                old = read_alignment(parts)
                if request.get('revision') != revision(old):
                    self.reply_json(409, {'error': '另一个窗口已保存对位，请重新打开编辑器再应用'})
                    return
                data = validate_alignment(request.get('alignment'), parts)
                parts['alignment'] = data
                page = (before + BEGIN + portrait_payload(parts) + END + after).encode()
                if len(page) > MAX_PAGE_BYTES:
                    raise ValueError('终端网页超出中继大小限制')
                previous = ALIGNMENT.read_bytes() if ALIGNMENT.exists() else None
                atomic_write(ALIGNMENT, (json.dumps(data, ensure_ascii=False, indent=2) + '\n').encode())
                try:
                    atomic_write(PAGE, page)
                except OSError:
                    if previous is None:
                        ALIGNMENT.unlink()
                    else:
                        atomic_write(ALIGNMENT, previous)
                    raise
                self.reply_json(200, {'alignment': data, 'revision': revision(data)})
        except (ValueError, TypeError) as error:
            self.reply_json(400, {'error': str(error)})
        except OSError as error:
            self.reply_json(500, {'error': str(error)})


def main():
    parser = argparse.ArgumentParser(description='本地立绘对位编辑器')
    parser.add_argument('--port', type=int, default=0)
    parser.add_argument('--no-browser', action='store_true')
    args = parser.parse_args()
    page_parts()
    with ThreadingHTTPServer(('127.0.0.1', args.port), Handler) as server:
        url = f'http://127.0.0.1:{server.server_port}/'
        print(url, flush=True)
        if not args.no_browser:
            webbrowser.open(url)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == '__main__':
    main()
