using System;

namespace Mono.Debugger.Soft
{
    public interface IInvokeAsyncResult : IAsyncResult
    {
        void Abort();
    }
}
