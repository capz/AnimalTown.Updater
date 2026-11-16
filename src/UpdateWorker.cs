using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.IO;
namespace AnimalTown.Updater;

internal static class UpdateWorker
{
    internal static Task<PreparedDol?> Run(CancellationToken token)
    {
        var worker=new Work(token);
        // Use the pinned SDK's Action overload explicitly. An awaited dummy
        // task with ConfigureAwait(false) could already be complete, allowing
        // the updater to start inline on the application executor instead.
        Task.Run((Action)worker.Start);
        return worker.Completion.Task;
    }
    private sealed class Work
    {
        private readonly CancellationToken token;
        private Task<PreparedDol?>? update;
        internal readonly TaskCompletionSource<PreparedDol?> Completion=new TaskCompletionSource<PreparedDol?>();
        internal Work(CancellationToken token){this.token=token;}
        internal void Start()
        {
            try{
                token.ThrowIfCancellationRequested();update=Updater.Run(token);
                if(update.IsCompleted)Finish();else update.GetAwaiter().OnCompleted(Finish);
            }catch(Exception error){Completion.SetException(error);}
        }
        private void Finish()
        {
            try{Completion.SetResult(update!.GetAwaiter().GetResult());}
            catch(Exception error){Completion.SetException(error);}
        }
    }
}
