namespace Mono.Debugger.Soft
{
	public interface IMethodBaseMirror : IMirrorWithId {
		bool IsConstructor { get; }
	}
}