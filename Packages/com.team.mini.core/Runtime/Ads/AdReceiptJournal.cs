using System;
using System.Collections.Generic;
using System.Linq;
using MiniFramework.Contracts;
namespace MiniFramework.Ads
{
    public sealed class AdReceiptJournal:IAdReceiptJournal
    {
        readonly Dictionary<string,AdPresentationReceipt> seen=new Dictionary<string,AdPresentationReceipt>(StringComparer.Ordinal);
        readonly HashSet<string> acknowledged=new HashSet<string>(StringComparer.Ordinal);readonly int capacity,maxTracked;
        public AdReceiptJournal(int capacity=64,int maxTracked=1024)
        {if(capacity<1||maxTracked<capacity||maxTracked>65536)throw new ArgumentOutOfRangeException();this.capacity=capacity;this.maxTracked=maxTracked;}
        public int PendingCount=>seen.Count-acknowledged.Count;
        public IReadOnlyList<AdPresentationReceipt> Pending=>Array.AsReadOnly(seen.Values.Where(r=>!acknowledged.Contains(r.AttemptId)).ToArray());
        public Result Record(AdPresentationReceipt receipt)
        {
            if(receipt==null)return Fail("ads.journal.argument",ErrorKind.InvalidArgument);
            if(seen.TryGetValue(receipt.AttemptId,out var old))return old.SameAs(receipt)?Result.Success():Fail("ads.journal.conflict",ErrorKind.Integrity);
            if(PendingCount>=capacity||seen.Count>=maxTracked)return Fail("ads.journal.capacity",ErrorKind.Unavailable);
            seen.Add(receipt.AttemptId,receipt);return Result.Success();
        }
        // Acknowledge only after the business has accepted/persisted the fact. No award logic here.
        public Result Acknowledge(string attemptId)
        {if(attemptId==null||!seen.ContainsKey(attemptId))return Fail("ads.journal.missing",ErrorKind.InvalidArgument);acknowledged.Add(attemptId);return Result.Success();}
        static Result Fail(string code,ErrorKind kind)=>Result.Failure(new FrameworkError(kind,code));
    }
}
