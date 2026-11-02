using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MiniFramework.Contracts;
namespace MiniFramework.Storage
{
    public sealed class StorageRecord
    {
        readonly byte[] data;public string ModuleId {get;}public int SchemaVersion {get;}public string CodecId {get;}public bool Required {get;}
        public int ByteCount=>data.Length;
        public StorageRecord(string moduleId,int schemaVersion,string codecId,bool required,byte[] payload)
        {if(!StorageIdentifier.IsValid(moduleId)||schemaVersion<=0||!StorageIdentifier.IsValid(codecId)||payload==null)throw new ArgumentException("Record invalid.");ModuleId=moduleId;SchemaVersion=schemaVersion;CodecId=codecId;Required=required;data=(byte[])payload.Clone();}
        public byte[] CopyPayload()=>(byte[])data.Clone();
    }
    public sealed class StorageContainer
    {
        public StorageAddress Address {get;}public long Sequence {get;}public IReadOnlyList<StorageRecord> Records {get;}
        public StorageContainer(StorageAddress address,long sequence,IEnumerable<StorageRecord> records)
        {
            if(!address.IsValid||sequence<=0||records==null)throw new ArgumentException("Container identity invalid.");
            var items=records.ToArray();if(items.Any(r=>r==null)||items.Select(r=>r.ModuleId).Distinct(StringComparer.Ordinal).Count()!=items.Length)throw new ArgumentException("Unique modules required.");
            Address=address;Sequence=sequence;Records=Array.AsReadOnly(items.OrderBy(r=>r.ModuleId,StringComparer.Ordinal).ToArray());
        }
    }
    // MINISAVE, uint32 format=1, ASCII ids (uint16 byte length), int64 sequence, int32 count.
    // Each record: id, int32 schema, codec id, byte required, int32 payload length, payload, SHA256.
    // Final SHA256 covers ALL preceding bytes. Integers are little-endian; no type names/hashes persist.
    public static class StorageFormat
    {
        public const int FormatVersion=1;static readonly byte[] Magic=Encoding.ASCII.GetBytes("MINISAVE");
        public static string Digest(byte[] bytes){if(bytes==null)throw new ArgumentNullException(nameof(bytes));using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(bytes).Select(b=>b.ToString("x2")));}
        static byte[] Hash(byte[] bytes){using(var sha=SHA256.Create())return sha.ComputeHash(bytes);}
        static void Id(BinaryWriter writer,string value){var bytes=Encoding.ASCII.GetBytes(value);writer.Write((ushort)bytes.Length);writer.Write(bytes);}
        static string Id(BinaryReader reader){var length=reader.ReadUInt16();if(length==0||length>64)throw new FormatException("id length");var bytes=reader.ReadBytes(length);if(bytes.Length!=length||bytes.Any(b=>b>127))throw new FormatException("id bytes");var value=Encoding.ASCII.GetString(bytes);if(!StorageIdentifier.IsValid(value))throw new FormatException("id value");return value;}
        public static Result<byte[]> Encode(StorageContainer container,StorageLimits limits)
        {
            if(container==null||limits==null)return Bad<byte[]>("storage.format.argument",ErrorKind.InvalidArgument);
            try
            {
                if(container.Records.Count>limits.MaxModules)throw new FormatException("module limit");
                // Calculate before growing a stream. Header max 8+4+2*(2+64)+8+4+32.
                long size=8+4+2+container.Address.Namespace.Length+2+container.Address.Slot.Length+8+4+32;
                foreach(var record in container.Records){if(record.ByteCount>limits.MaxPayloadBytes)throw new FormatException("payload limit");size+=2+record.ModuleId.Length+4+2+record.CodecId.Length+1+4+record.ByteCount+32;}
                if(size>limits.MaxContainerBytes)throw new FormatException("container limit");
                using(var stream=new MemoryStream((int)size))using(var writer=new BinaryWriter(stream,Encoding.ASCII,true))
                {
                    writer.Write(Magic);writer.Write(FormatVersion);Id(writer,container.Address.Namespace);Id(writer,container.Address.Slot);writer.Write(container.Sequence);writer.Write(container.Records.Count);
                    foreach(var record in container.Records){Id(writer,record.ModuleId);writer.Write(record.SchemaVersion);Id(writer,record.CodecId);writer.Write((byte)(record.Required?1:0));var payload=record.CopyPayload();writer.Write(payload.Length);writer.Write(payload);writer.Write(Hash(payload));}
                    writer.Flush();var body=stream.ToArray();writer.Write(Hash(body));writer.Flush();return Result<byte[]>.Success(stream.ToArray());
                }
            }
            catch(Exception e) when(e is ArgumentException||e is FormatException||e is OverflowException){return Bad<byte[]>("storage.format.limit",ErrorKind.Integrity);}
        }
        public static Result<StorageContainer> Decode(byte[] bytes,StorageLimits limits)
        {
            if(bytes==null||limits==null)return Bad<StorageContainer>("storage.format.argument",ErrorKind.InvalidArgument);
            if(bytes.Length<62||bytes.Length>limits.MaxContainerBytes)return Bad<StorageContainer>("storage.format.length");
            try
            {
                var body=new byte[bytes.Length-32];Buffer.BlockCopy(bytes,0,body,0,body.Length);
                var checksum=Hash(body);for(var i=0;i<32;i++)if(checksum[i]!=bytes[body.Length+i])return Bad<StorageContainer>("storage.format.checksum");
                using(var stream=new MemoryStream(body,false))using(var reader=new BinaryReader(stream,Encoding.ASCII,true))
                {
                    if(!reader.ReadBytes(8).SequenceEqual(Magic))return Bad<StorageContainer>("storage.format.magic");
                    if(reader.ReadInt32()!=FormatVersion)return Bad<StorageContainer>("storage.format.unsupported");
                    var address=new StorageAddress(Id(reader),Id(reader));var sequence=reader.ReadInt64();if(sequence<=0)throw new FormatException("sequence");
                    var count=reader.ReadInt32();if(count<0||count>limits.MaxModules)throw new FormatException("module count");
                    var records=new List<StorageRecord>(count);string previous=null;
                    for(var i=0;i<count;i++)
                    {
                        var id=Id(reader);if(previous!=null&&StringComparer.Ordinal.Compare(previous,id)>=0)throw new FormatException("module order");previous=id;
                        var schema=reader.ReadInt32();var codec=Id(reader);var required=reader.ReadByte();var size=reader.ReadInt32();
                        if(schema<=0||required>1||size<0||size>limits.MaxPayloadBytes||size>stream.Length-stream.Position-32)throw new FormatException("record header");
                        var payload=reader.ReadBytes(size);var digest=reader.ReadBytes(32);if(!digest.SequenceEqual(Hash(payload)))return Bad<StorageContainer>("storage.format.payload_checksum");
                        records.Add(new StorageRecord(id,schema,codec,required==1,payload));
                    }
                    if(stream.Position!=stream.Length)throw new FormatException("trailing bytes");return Result<StorageContainer>.Success(new StorageContainer(address,sequence,records));
                }
            }
            catch(Exception e) when(e is ArgumentException||e is FormatException||e is EndOfStreamException||e is OverflowException){return Bad<StorageContainer>("storage.format.invalid");}
        }
        static Result<T> Bad<T>(string code,ErrorKind kind=ErrorKind.Integrity)=>Result<T>.Failure(new FrameworkError(kind,code));
    }
}
