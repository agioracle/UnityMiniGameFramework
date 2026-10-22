# Git标签选择、升级与回滚

安装0.2.0：Package Manager > + > Add package from git URL：

`https://github.com/agioracle/UnityMiniGameFramework.git?path=/Packages/com.minigame.framework#v0.2.0`

升级时输入同一包的新已发布Tag URL，回滚输入旧已发布Tag URL。也可在自己的manifest中改变该包URL最后#revision；UPM重新解析并记录完整commit。不要填缩写SHA，不移动既有Tag，不依赖默认分支自动跟随。没有发布的v0.2.1仅可作为格式示例，不能声称可安装。

UPM更新包，不自动覆盖Assets/Samples中的用户资产或玩家数据；示例使用既有GUID，重复导入前比较/备份，不自动跨目录重复导入。游戏schema迁移由消费者决定，回滚代码不承诺业务数据可逆。

从0.1.x迁移：备份消费者工程，移除旧框架及com.minigame.dependencies.r3、com.cysharp.unitask、com.cysharp.r3、com.tuyoogame.yooasset包和重复DLL，再添加0.2.x URL。内置版不能与独立同名程序集并存；不通过脚本删除业务Assets/存档。原0.1.x套件冻结和回滚验收历史保持，但不等于GitURL版本已验证。

准确第三方版本与源码摘要位于Documentation~/third-party-lock.json。YooAsset自身PackageInfo显示外层框架版本，不作为YooAsset兼容判断，官方源未修改。
