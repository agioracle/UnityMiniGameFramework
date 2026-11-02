using System;
namespace MiniFramework.Contracts
{
    public enum ErrorKind
    {
        None, InvalidResult, InvalidArgument, InvalidState, NotReady, NotSupported,
        Unavailable, PermissionDenied, NoFill, Network, Storage, Integrity,
        Dependency, Provider, Unexpected, Canceled, Timeout, CleanupFailed
    }
    // A hint to the caller, never permission to replay non-idempotent writes.
    public enum RetryHint { Never, MayRetry, AfterUserAction }
    public readonly struct FrameworkError
    {
        public ErrorKind Kind { get; }
        public string Code { get; }
        public string Message { get; }
        public RetryHint Retry { get; }
        public FrameworkError(ErrorKind kind, string code, string message = "", RetryHint retry = RetryHint.Never)
        {
            if (!Enum.IsDefined(typeof(ErrorKind), kind) || kind == ErrorKind.None) throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A stable diagnostic code is required.", nameof(code));
            if (!Enum.IsDefined(typeof(RetryHint), retry)) throw new ArgumentOutOfRangeException(nameof(retry));
            Kind = kind; Code = code; Message = message ?? ""; Retry = retry;
        }
        public static FrameworkError Uninitialized => new FrameworkError(ErrorKind.InvalidResult, "core.result.uninitialized");
    }
    public readonly struct Unit { }
    // Zero-initialized results are explicitly NOT successful.
    public readonly struct Result<T>
    {
        private readonly byte state;
        private readonly T value;
        private readonly FrameworkError error;
        private Result(T value) { state = 1; this.value = value; error = default; }
        private Result(FrameworkError error) { state = 2; value = default; this.error = error; }
        public bool IsSuccess => state == 1;
        public T Value => IsSuccess ? value : throw new InvalidOperationException("A failed result has no value.");
        public FrameworkError Error => state == 0 ? FrameworkError.Uninitialized :
            state == 2 ? error : throw new InvalidOperationException("A successful result has no error.");
        public static Result<T> Success(T value) => new Result<T>(value);
        public static Result<T> Failure(FrameworkError error)
        {
            if (error.Kind == ErrorKind.None) throw new ArgumentException("Default error is not a failure.", nameof(error));
            return new Result<T>(error);
        }
    }
    public readonly struct Result
    {
        private readonly Result<Unit> inner;
        private Result(Result<Unit> inner) { this.inner = inner; }
        public bool IsSuccess => inner.IsSuccess;
        public FrameworkError Error => inner.Error;
        public static Result Success() => new Result(Result<Unit>.Success(default));
        public static Result Failure(FrameworkError error) => new Result(Result<Unit>.Failure(error));
    }
}
