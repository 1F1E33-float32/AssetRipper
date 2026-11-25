using System.Diagnostics;
using System.Runtime.CompilerServices;
using AssetRipper.Assets;
using AssetRipper.Assets.Cloning;
using AssetRipper.Assets.IO.Writing;
using AssetRipper.Assets.Metadata;
using AssetRipper.Assets.Traversal;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.Import.Structure.Assembly.TypeTrees;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files.SerializedFiles.Parser.TypeTrees;
using AssetRipper.SerializationLogic;

namespace AssetRipper.Import.AssetCreation;

[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
public abstract class TypeTreeObject : NullObject
{
	private static readonly ConditionalWeakTable<TypeTree, Layout> Layouts = new();

	private sealed record Layout(TypeTreeNodeStruct Root, SerializableTreeType Type);

	/// <summary>Exact source bytes, including padding, for a fully parsed original object.</summary>
	public byte[]? OriginalData { get; internal set; }
	public TypeTree? OriginalTypeTree { get; internal set; }
	public string? OriginalResourcePath { get; internal set; }
	public bool IsPlayerSettings => ClassID == 129;
	public abstract SerializableStructure ReleaseFields { get; }
	public abstract SerializableStructure EditorFields { get; }
	public sealed override bool FlowMappedInYaml => EditorFields.FlowMappedInYaml;
	public sealed override int SerializedVersion => EditorFields.SerializedVersion;
	public sealed override string ClassName => EditorFields.Type.Name;

	private TypeTreeObject(AssetInfo assetInfo)
		: base(assetInfo) { }

	public override void WriteRelease(AssetWriter writer) => ReleaseFields.WriteRelease(writer);

	public override void WriteEditor(AssetWriter writer) => EditorFields.WriteEditor(writer);

	public sealed override void WalkRelease(AssetWalker walker) => ReleaseFields.WalkRelease(walker);

	public sealed override void WalkEditor(AssetWalker walker) => EditorFields.WalkEditor(walker);

	public static TypeTreeObject Create(AssetInfo assetInfo, TypeTreeNodeStruct root, ITypeResolver resolver) =>
		new SingleTypeTreeObject(assetInfo, root, resolver);

	public static TypeTreeObject Create(AssetInfo assetInfo, TypeTree tree, ITypeResolver resolver)
	{
		Layout layout = Layouts.GetValue(
			tree,
			static tree =>
			{
				if (!TypeTreeNodeStruct.TryMakeFromTypeTree(tree, out TypeTreeNodeStruct root))
				{
					throw new InvalidDataException("The source object has no type tree.");
				}
				return new(root, SerializableTreeType.FromRootNode(root));
			}
		);
		return new SingleTypeTreeObject(assetInfo, layout.Root, resolver, layout.Type);
	}

	public static TypeTreeObject Create(AssetInfo assetInfo, TypeTreeNodeStruct releaseRoot, TypeTreeNodeStruct editorRoot, ITypeResolver resolver) =>
		new DoubleTypeTreeObject(assetInfo, releaseRoot, editorRoot, resolver);

	private string GetDebuggerDisplay() => ClassName;

	private sealed class SingleTypeTreeObject : TypeTreeObject, INamed
	{
		private readonly ITypeResolver resolver;
		private readonly TypeTreeNodeStruct root;
		public SerializableStructure Fields { get; }
		public override SerializableStructure ReleaseFields => Fields;
		public override SerializableStructure EditorFields => Fields;
		public Utf8String Name
		{
			get => TryGetNameIndex(out int index) ? Fields.Fields[index].AsString : Utf8String.Empty;
			set
			{
				if (TryGetNameIndex(out int index))
				{
					Fields.Fields[index].AsString = value;
				}
			}
		}

		private bool TryGetNameIndex(out int index) =>
			Fields.TryGetIndex("m_Name", out index)
			&& Fields.Type.Fields[index].Type.Type == PrimitiveType.String
			&& Fields.Type.Fields[index].ArrayDepth == 0;

		public SingleTypeTreeObject(AssetInfo assetInfo, TypeTreeNodeStruct root, ITypeResolver resolver, SerializableTreeType? type = null)
			: base(assetInfo)
		{
			this.resolver = resolver;
			this.root = root;
			Fields = new(type ?? SerializableTreeType.FromRootNode(root), 0, Collection.Version);
		}

		public override void ReadRelease(ref EndianSpanReader reader)
		{
			TypeTreeSerialization.Read(Fields, root, ref reader, Collection.Version, Collection.Flags, resolver);
		}

		public override void WriteRelease(AssetWriter writer) => TypeTreeSerialization.Write(writer, root, Fields.Type, Fields);

		public override void WriteEditor(AssetWriter writer) => TypeTreeSerialization.Write(writer, root, Fields.Type, Fields);

		public override void ReadEditor(ref EndianSpanReader reader)
		{
			TypeTreeSerialization.Read(Fields, root, ref reader, Collection.Version, Collection.Flags, resolver);
		}

		public override void WalkStandard(AssetWalker walker)
		{
			Fields.WalkStandard(walker);
		}

		public override void Reset()
		{
			Fields.Reset();
		}

		public override IEnumerable<(string, PPtr)> FetchDependencies()
		{
			return Fields.FetchDependencies();
		}
	}

	private sealed class DoubleTypeTreeObject : TypeTreeObject
	{
		private readonly ITypeResolver resolver;
		public override SerializableStructure ReleaseFields { get; }
		public override SerializableStructure EditorFields { get; }

		public DoubleTypeTreeObject(AssetInfo assetInfo, TypeTreeNodeStruct releaseRoot, TypeTreeNodeStruct editorRoot, ITypeResolver resolver)
			: base(assetInfo)
		{
			this.resolver = resolver;
			ReleaseFields = SerializableTreeType.FromRootNode(releaseRoot).CreateSerializableStructure();
			EditorFields = SerializableTreeType.FromRootNode(editorRoot).CreateSerializableStructure();
		}

		public override void ReadRelease(ref EndianSpanReader reader)
		{
			ReleaseFields.Read(ref reader, Collection.Version, Collection.Flags, resolver);
			ConvertFields(ReleaseFields, EditorFields);
		}

		public override void ReadEditor(ref EndianSpanReader reader)
		{
			EditorFields.Read(ref reader, Collection.Version, Collection.Flags, resolver);
			ConvertFields(EditorFields, ReleaseFields);
		}

		public override void WalkStandard(AssetWalker walker)
		{
			if (walker.EnterAsset(this))
			{
				if (walker.EnterField(this, "Release"))
				{
					ReleaseFields.WalkStandard(walker);
					walker.ExitField(this, "Release");
				}
				walker.DivideAsset(this);
				if (walker.EnterField(this, "Editor"))
				{
					EditorFields.WalkStandard(walker);
					walker.ExitField(this, "Editor");
				}
				walker.ExitAsset(this);
			}
		}

		public override void Reset()
		{
			ReleaseFields.Reset();
			EditorFields.Reset();
		}

		public override IEnumerable<(string, PPtr)> FetchDependencies()
		{
			return ReleaseFields.FetchDependencies().Union(EditorFields.FetchDependencies());
		}

		private void ConvertFields(SerializableStructure source, SerializableStructure target)
		{
			target.CopyValues(source, new PPtrConverter(Collection, Collection));
		}
	}
}
