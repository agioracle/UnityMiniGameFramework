using System;
using System.Collections.Generic;
using Game.Config.Generated;
using Luban;
using MiniFramework.Config;
namespace Game.Config
{
    public static class GameConfigDecoder
    {
        public static ConfigDefinition Definition(string provider="local",string package="main",string version="v1")=>new ConfigDefinition(provider,package,version,GeneratedConfigInfo.Schema,"config/catalog",new[]{"tblevel","tbreward"});
        public static Tables Decode(IReadOnlyDictionary<string,byte[]> files)
        {
            var buffers=new List<ByteBuf>();
            var tables=new Tables(name=>{var buffer=new ByteBuf(files[name]);buffers.Add(buffer);return buffer;});
            foreach(var buffer in buffers)if(buffer.Size!=0)throw new FormatException("Trailing config bytes.");
            foreach(var reward in tables.TbReward.DataList)if(reward.Id<=0||reward.Coins<=0||reward.Coins>1000000)throw new FormatException("Reward range.");
            foreach(var level in tables.TbLevel.DataList)if(level.Id<=0||level.RewardId_Ref==null||string.IsNullOrWhiteSpace(level.SceneAddress)||!level.SceneAddress.StartsWith("levels/",StringComparison.Ordinal))throw new FormatException("Level references.");
            return tables;
        }
    }
}
