# Changelog

## 0.2.0 — 2026-10-22

发行布局变更：单个UPM Git URL可安装，URL的#tag固定框架版本。内置官方未修改UniTask2.5.11、R3Unity/Core1.3.0与YooAsset2.3.17，第三方LICENSE/NOTICE/GUID保留。顶层仅依赖Unity官方模块/uGUI/TMP/SBP1.21.25，不在包清单中声明不支持的Git间接依赖。

0.1.x迁移必须先移除外部旧依赖包及重复DLL；这属于预览期分发边界变化，升minor，不原地覆盖旧版。Runtime公开接口不为本次安装调整重构。YooAsset内部PackageInfo返回外层发行版本，上游准确版本在锁记录，不修改第三方函数掩盖差异。

## 历史

0.1.3/0.1.2本地双包发行的升级/回滚证据仍保留，不冒充GitURL安装验证。0.1.0缺依赖编译失败、0.1.1消费者测试未发现的历史记录不修改。0.2.x未来修复升patch，Tag固定内容，不移动已有Tag、不强制推送。
