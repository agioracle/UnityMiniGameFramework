# Package Manager Git安装

在Unity2022.3.62f3中：Window > Package Manager > + > Add package from git URL，输入

`https://github.com/agioracle/UnityMiniGameFramework.git?path=/Packages/com.minigame.framework#v0.2.0`

仅一个URL，不需要Python安装脚本或先添加第三方Git依赖。Unity官方registry依赖自动解析，Git和网络仍必要。#tag在?path后，标签固定commit，必须真实存在；安装/升级时选择新的已发布Tag，回滚选旧Tag。不要通过移动Tag更新原版本。

Samples > Basic Playable > Import导入到Unity的版本化Samples目录，运行Scenes/Boot.unity。升级只更新PackageCache，Assets用户样例和业务数据不重写，重导同GUID样例前比较现有内容。

0.2.0内置第三方库，不与外部UniTask/R3/YooAsset/R3Core同时安装。已用0.1.x时备份工程，移除对应5个旧包/重复DLL再添加新URL；框架不自动删除业务文件或替用户迁移依赖。新项目无需上述清理。

Tag发布与Git方式Unity安装验收分别记录，不以Git上传成功代替编译/PlayMode验证。旧0.1.x套件证据不作为0.2.x验证。
