using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Access;
using MiniFramework.Contracts;
namespace Game.Revive
{
    public static class ReviveCodec
    {
        public const string ModuleId="game.revive",CodecId="revive.bin.v1";public const int Schema=1,MaxBytes=65536;
        static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        static void Text(BinaryWriter writer,string value,int max){var bytes=Utf8.GetBytes(value);if(bytes.Length>max)throw new FormatException();writer.Write((ushort)bytes.Length);writer.Write(bytes);}
        static string Text(BinaryReader reader,int max){var count=reader.ReadUInt16();if(count>max||count>reader.BaseStream.Length-reader.BaseStream.Position)throw new FormatException();return Utf8.GetString(reader.ReadBytes(count));}
        static bool Flag(BinaryReader reader){var value=reader.ReadByte();if(value>1)throw new FormatException();return value==1;}
        public static Result<byte[]> Encode(ReviveSnapshot value)
        {
            if(value==null)return Result<byte[]>.Failure(Error());
            try
            {
                using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory,Utf8,true))
                {
                    writer.Write(new byte[]{77,82,86,49});writer.Write((byte)value.Mode);Text(writer,value.OwnerId,128);Text(writer,value.Namespace,64);writer.Write(value.LastGeneration);writer.Write(value.Coins);Text(writer,value.RunId,128);writer.Write(value.Level);writer.Write((byte)value.Phase);writer.Write((byte)(value.ReviveUsed?1:0));writer.Write((byte)(value.Collected?1:0));writer.Write(value.HighestCollectedLevel);writer.Write((ushort)value.Claims.Count);
                    foreach(var c in value.Claims){Text(writer,c.ClaimId,128);Text(writer,c.AttemptId,128);Text(writer,c.RunId,128);writer.Write((byte)c.Owner.Mode);Text(writer,c.Owner.OwnerId,128);writer.Write(c.Owner.SessionGeneration);Text(writer,c.Owner.StorageNamespace,64);writer.Write(c.Level);writer.Write(c.RewardCoins);writer.Write((byte)c.AdMode);writer.Write((byte)c.State);writer.Write(c.GrantSequence);}
                    writer.Flush();if(memory.Length>MaxBytes)throw new FormatException();return Result<byte[]>.Success(memory.ToArray());
                }
            }
            catch(Exception){return Result<byte[]>.Failure(Error());}
        }
        public static Result<ReviveSnapshot> Decode(byte[] bytes)
        {
            if(bytes==null||bytes.Length>MaxBytes)return Result<ReviveSnapshot>.Failure(Error());
            try
            {
                using(var memory=new MemoryStream(bytes,false))using(var reader=new BinaryReader(memory,Utf8,true))
                {
                    if(reader.ReadByte()!=77||reader.ReadByte()!=82||reader.ReadByte()!=86||reader.ReadByte()!=49)throw new FormatException();
                    var mode=(ClaimOwnerMode)reader.ReadByte();var owner=Text(reader,128);var ns=Text(reader,64);var gen=reader.ReadInt64();var coins=reader.ReadInt64();var run=Text(reader,128);var level=reader.ReadInt32();var phase=(ReviveRunPhase)reader.ReadByte();var used=Flag(reader);var collected=Flag(reader);var highest=reader.ReadInt32();var count=reader.ReadUInt16();if(count>ReviveSnapshot.MaxClaims)throw new FormatException();var claims=new List<ReviveClaim>(count);
                    for(var i=0;i<count;i++)
                    {
                        var claim=Text(reader,128);var attempt=Text(reader,128);var claimRun=Text(reader,128);var cm=(ClaimOwnerMode)reader.ReadByte();var co=Text(reader,128);var cg=reader.ReadInt64();var cn=Text(reader,64);var cl=reader.ReadInt32();var reward=reader.ReadInt32();var ad=(PlatformExecutionMode)reader.ReadByte();var state=(ReviveClaimState)reader.ReadByte();var sequence=reader.ReadInt64();
                        claims.Add(new ReviveClaim(claim,attempt,claimRun,new ClaimOwnerSnapshot(cm,co,cg,cn),cl,reward,ad,state,sequence));
                    }
                    if(memory.Position!=memory.Length)throw new FormatException();return Result<ReviveSnapshot>.Success(new ReviveSnapshot(mode,owner,ns,gen,coins,run,level,phase,used,collected,highest,claims));
                }
            }
            catch(Exception){return Result<ReviveSnapshot>.Failure(Error());}
        }
        static FrameworkError Error()=>new FrameworkError(ErrorKind.Integrity,"revive.codec.invalid");
    }
}
