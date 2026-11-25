using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

using CreateCCWFunc = Il2CppMethodPointer;
using PInvokeMarshalCleanupFunc = Il2CppMethodPointer;
using PInvokeMarshalFromNativeFunc = Il2CppMethodPointer;
using PInvokeMarshalToNativeFunc = Il2CppMethodPointer;

[VersionedStruct]
public partial record struct Il2CppInteropData
{
	public Il2CppMethodPointer DelegatePInvokeWrapperFunction;
	public Il2CppMethodPointer PInvokeMarshalToNativeFunction;
	public Il2CppMethodPointer PInvokeMarshalFromNativeFunction;
	public Il2CppMethodPointer PInvokeMarshalCleanupFunction;
	public Il2CppMethodPointer CreateCCWFunction;
	public Pointer<Il2CppGuid> Guid;
	public Pointer<Il2CppType> Type;
}
