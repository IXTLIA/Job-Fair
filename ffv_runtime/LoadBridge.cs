using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Il2CppInterop.Runtime;

namespace ProjectBlues.Runtime
{
    // The native UniTask is single-consumer and this build strips Preserve().
    // We consume it exactly once and give the game a separate completion task.
    internal sealed class LoadBridge
    {
        internal readonly RunMetadata Metadata;
        private readonly AutoResetUniTaskCompletionSource completion;
        private UniTask.Awaiter awaiter;
        private Il2CppSystem.Action continuation;
        private int completed;
        internal UniTask Proxy => completion.Task;

        internal LoadBridge(RunMetadata metadata)
        {
            Metadata = metadata;
            completion = AutoResetUniTaskCompletionSource.Create();
        }

        internal void Start(UniTask original)
        {
            try
            {
                awaiter = original.GetAwaiter();
                if (awaiter.IsCompleted) Complete();
                else
                {
                    continuation = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)Complete);
                    awaiter.UnsafeOnCompleted(continuation);
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void Complete()
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) return;
            try
            {
                awaiter.GetResult();
                RunSession.CompleteLoad(this);
            }
            catch (Exception exception)
            {
                FailCore(exception);
                return;
            }
            // Commit the run before releasing the game's original load flow.
            // Do not retain or inspect this pooled completion after this call.
            completion.TrySetResult();
        }

        private void Fail(Exception exception)
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) return;
            FailCore(exception);
        }

        private void FailCore(Exception exception)
        {
            Plugin.Logger.LogError(exception);
            RunSession.FailLoad("The save could not finish loading. Saving is blocked to protect it. Please close FFV and report the error.");
            // TrySetException is stripped from this game's generated source.
            // Cancellation propagates failure through a verified public API.
            completion.TrySetCanceled(Il2CppSystem.Threading.CancellationToken.None);
        }
    }
}
