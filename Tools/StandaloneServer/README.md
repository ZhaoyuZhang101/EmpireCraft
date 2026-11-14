# 独立服务器(终端核心)：本地编译，一键传到服务器并常驻

## 平时怎么用

在模组目录里双击 `update_server.bat`(或在 PowerShell 里 `.\update_server.bat`)。它依次：

1. 本地编译出 Linux 版程序(调 `.server\publish.ps1`，自包含，服务器上不用装 dotnet)；
2. 把程序和核心要读的数据(`Terminal`、`WorldMaps`、`Locales`、`Technology`、`InstitutionTrees`、`Scripts` 里的配置文件、根目录的几份 json)压成一个包(四十来兆)；
3. 传到服务器(上传时显示百分比、速度、剩余时间)，在服务器上解到 `/opt/empirecraft/.incoming` 并跑 `install.sh`：换上新程序和新数据，重启服务，确认它起来了。新版一分钟内没起来会自动换回上一版。

服务器上的存放目录(`server.json`、账号、上传的剧本、存档)不会被覆盖。

- `.\update_server.bat nobuild`：不重新编译，把上次编译好的再传一遍。
- `.\update_server.bat check`：只从这台电脑问一下核心在不在(端口照脚本顶上的 `CORE_PORT`，现在是 8766)。
- 服务器地址、用户名、目录在 `update_server.bat` 顶上改。

每次更新都会重启核心。核心停的时候会先存档，起来后接着存档跑；没有存档时才照 `server.json` 里写的剧本开局(`scenario` 空着就等管理员在房间里挑)。

## 只做一次：让 SSH 不再问密码

在自己电脑的 PowerShell 里：

```powershell
ssh-keygen -t ed25519
```

一路回车。然后把公钥放到服务器上(这一步会最后问一次服务器密码)：

```powershell
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh root@8.159.129.136 "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

之后 `update_server.bat`、`update_lobby.bat` 都不再问密码。

## 常驻

`install.sh` 把核心装成系统服务 `empirecraft-core`：开机自启，进程退出(崩溃、被杀)五秒后自动重启，关掉 SSH 窗口也照常跑。在服务器上：

```bash
systemctl status empirecraft-core      # 在不在跑
journalctl -u empirecraft-core -f      # 看日志(没设管理员账号密码时，管理员码也打印在这里)
systemctl restart empirecraft-core     # 重启(先存档，起来接着跑)
systemctl stop empirecraft-core        # 停
```

装成服务以后没有命令行输入，`status`、`accounts` 这些控制台命令用不了，改用管理员账号在网页里管。

## 服务器上的目录

```
/opt/empirecraft/app/          程序(每次更新整个换掉；上一版在 app.old)
/opt/empirecraft/Terminal 等   数据(每次更新整个换掉)
/opt/empirecraft/core-state/   存放目录：server.json、账号、上传的剧本、存档(不动)
```

存放目录沿用服务现在用的那个；头一回装时取 `core-state`、`server-state`、`state` 里有 `server.json` 的头一个。以前传上去的 `.server` 源码目录和 `/opt/dotnet` 用不上了，确认新版跑稳后可以自己删。

核心的端口写在存放目录的 `server.json` 里(`"port"`)。和联机大厅同机时别用 8765(大厅占着)；端口开不了时核心会在日志里明说并停下，改了端口再 `systemctl restart empirecraft-core`。阿里云的安全组要在控制台放行这个端口。
