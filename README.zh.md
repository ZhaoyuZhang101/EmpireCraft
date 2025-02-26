<h1 align="center">
  <img src="https://raw.githubusercontent.com/WorldBoxOpenMods/ModTemplate/master/icon.png" alt="logo" width="200">
  <br/>
  ModTemplate
</h1>

<p align="center">
  <a href="https://github.com/WorldBoxOpenMods/ModTemplate/blob/master/README.zh.md"><img alt="zh" src="https://img.shields.io/badge/zh-简体中文-red.svg"></a>
  <a href="https://github.com/WorldBoxOpenMods/ModTemplate/blob/master/README.md"><img alt="en" src="https://img.shields.io/badge/en-English-green.svg"></a>
</p>

这是一个由NeoModLoader加载的通用WorldBox mod的模板.

点击右上角的绿色按钮"Use this template"来使用这个模板创建你自己的模组仓库.

## 如何制作你自己的模组

这是一个模板仓库, 你可以使用它来创建你自己的模组仓库.

在 [这](https://github.com/WorldBoxOpenMods/ModLoader/wiki/Mod%E5%88%B6%E4%BD%9C%E6%95%99%E7%A8%8B%E4%B8%BB%E9%A1%B5)
有模组制作的简单教程.

## 一些重要的更改

定位到文件 `mod.json` 并更改以下字段

1. "name": 模组名字, 注意不要有换行之类的特殊字符
2. "author": 模组作者, 注意不要有换行之类的特殊字符
3. "description": 模组的简单介绍
4. "RepoURL": 模组仓库的URL.

定位到文件 `ModClass.cs` 并把"CHANGEME"改成你自己的模组的命名空间.