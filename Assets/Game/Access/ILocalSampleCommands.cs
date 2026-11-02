using MiniFramework.Contracts;
namespace Game.Access
{
    public interface ILocalSampleCommands
    {
        Result RequestLevel(int id);
        Result RequestHome();
        Result RequestRetry();
        Result Collect();
        Result ToggleMute();
    }
}
