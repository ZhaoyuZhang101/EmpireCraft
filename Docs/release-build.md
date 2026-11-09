# 发布版打包：编译成 DLL + 保守混淆

（用户 2026-11-08「把模组编译成 dll 再加上加密，每次发布版本的时候」「保守混淆」「仓库保持公开，只打包 DLL」）

## 这套流程能做到什么、做不到什么

- **能**：发布包里只放编译好的 `EmpireCraft.dll`，不放任何 `.cs`。别人拿到的是编译产物，要反编译才能看，日常抄代码的门槛大幅抬高。游戏（NML）照常加载。
- **做不到「只有我能看」**：
  1. DLL 能被游戏运行，就能被 dnSpy / ILSpy 反编译。混淆只是把反编译结果弄得很难读，不是真加密。
  2. 你的 GitHub 仓库是**公开**的，全部源码在 GitHub 上人人可见。你选择“仓库保持公开”，所以发布包打成 DLL 只能防“从创意工坊订阅后直接拿 .cs”，防不住看 GitHub。真要彻底保密得把仓库转私有。
- 数据资源（`Locales`、`Technology`、各 `json`、`GameResources` 等）是数据不是代码，**照常明文发布**，混淆只作用于 C# 逻辑。

## NML 为什么能加载纯 DLL 模组

读 NML 源码 `services/ModCompileLoadService.cs`（master 分支）确认：模组文件夹**根目录只要有任意一个 `.dll`**，NML 就把它判为 `COMPILED_NEOMOD`、**跳过编译**，`Assembly.Load` 这个 DLL，`.cs` 根本不会被读取；入口类用反射找（继承 `MonoBehaviour` + 实现 `IMod`，即本模组的 `ModClass`）。所以发布包放一个 `EmpireCraft.dll`、不放源码即可。

> 要点：NML 会加载**根目录下的每个 `.dll`**。所以发布包根目录**只能有 `EmpireCraft.dll` 这一个 DLL**，绝不能把 `0Harmony.dll`、`Newtonsoft.Json.dll` 等依赖也拷进去（会和游戏/NML 已加载的副本冲突）。打包脚本已做校验，多一个 dll 会直接报错中止。

## 前置

- 在**你本机**（装了 WorldBox 的 Windows）上跑。云端/CI 没有游戏程序集，编译不了。
- 装 .NET SDK（`dotnet`）。
- 混淆器：`dotnet tool install --global Obfuscar.GlobalTool`

## 每次发布怎么做

在仓库根目录：

```powershell
pwsh build/pack-release.ps1
```

产物：
- `dist/EmpireCraft/` —— 可直接当模组文件夹用的发布目录；
- `dist/EmpireCraft-<版本>.zip` —— 压缩包。

版本号取自 `mod.json` 的 `version`，所以发布前先把 `mod.json` 的版本改好。

只想编译成 DLL、暂不混淆：

```powershell
pwsh build/pack-release.ps1 -NoObfuscate
```

## 发布前必测（混淆最容易在这里翻车）

把 `dist/EmpireCraft/` 装进游戏当模组，**至少测两项**：

1. **开新世界**：模组正常加载（日志有 `[NML][EmpireCraft] ... Loaded`），帝国/派系/爵位/法理、各神力、律法、科技回退等功能正常。
2. **读一个旧存档**：这是重点。本模组把大量自定义数据写进存档，若混淆改了这些类名/字段名，旧档会读不出或丢数据。能正常读档、数据还在，才算过。

另外扫一眼游戏日志有没有 `FileNotFoundException`/`TypeLoadException` 之类（见下“排错”）。

## 排错

### 开了混淆就报错 / 读不出旧档
说明某个被序列化或被反射用到的名字被改了。到 `build/Obfuscar.xml` 的 `<Module>` 里把对应部分排除，重新打包：
- 整个命名空间不改名：`<SkipNamespace name="EmpireCraft.Scripts.Xxx" />`
- 某个类不改名：`<SkipType name="EmpireCraft.Scripts.Xxx.Yyy" />`
- 某个方法不改名：`<SkipMethod type="..." name="..." />`

默认已排除 `Scripts/Data` 命名空间、以及名字以 `Data`/`State` 结尾的类型（存档数据多在这些里）。日志报哪个类型找不到/对不上，就把那个加进排除表。

### 加载时报缺某个程序集（FileNotFoundException: Microsoft.CodeAnalysis... / SignalR... 之类）
csproj 里引用了一些可能运行时并不需要的包。两种处理：
- 若确实没用到：从 `EmpireCraft.csproj` 删掉那条 `PackageReference`；
- 若真的要用：把该依赖用 ILRepack 合并进 `EmpireCraft.dll`（不要单独拷进模组根目录）。

### 想再混得狠一点
`build/Obfuscar.xml` 末尾有说明：打开字段改名、去掉类型兜底排除、或在此之后再叠一层 ConfuserEx2（控制流/加壳）。每加一档都要重做上面的“必测”，尤其读旧档。

## 不进发布包的东西（打包脚本用白名单，自动排除）

`Scripts/`（源码）、`EmpireCraft.csproj/.sln`、`Properties/`、`Tools/`、`Docs/`、`Verification/`、`.github/`、`bin/`、`obj/` 等一律不进包。脚本还会在压包前校验：根目录只有一个 dll、且全包无 `.cs`，否则报错中止。
