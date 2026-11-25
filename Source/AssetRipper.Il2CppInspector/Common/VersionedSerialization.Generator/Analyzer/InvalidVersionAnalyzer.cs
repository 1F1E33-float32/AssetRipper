using System.Collections.Immutable;
using AssetRipper.Il2CppInspector.VersionedSerialization.Generator.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AssetRipper.Il2CppInspector.VersionedSerialization.Generator.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class InvalidVersionAnalyzer : DiagnosticAnalyzer
{
	private const string Identifier = "VS0001";
	private const string Category = "Usage";
	private const string Title = "Invalid version string in attribute";
	private const string MessageFormat = "Invalid version string";
	private const string Description = "The version needs to be specified in the following format: <major>.<minor>. Tags are not supported.";
	private static readonly DiagnosticDescriptor Descriptor = new(
		Identifier,
		Title,
		MessageFormat,
		Category,
		DiagnosticSeverity.Error,
		true,
		Description
	);
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.PropertyDeclaration, SyntaxKind.FieldDeclaration);
	}

	private static void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
	{
		if (context.ContainingSymbol == null)
		{
			return;
		}

		Compilation compilation = context.Compilation;
		INamedTypeSymbol? versionConditionAttribute = compilation.GetTypeByMetadataName(Constants.VersionConditionAttribute);
		foreach (AttributeData attribute in context.ContainingSymbol.GetAttributes())
		{
			if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, versionConditionAttribute))
			{
				if (attribute.ApplicationSyntaxReference == null)
				{
					continue;
				}

				foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
				{
					string name = argument.Key;
					if (
						name
						is Constants.LessThan
							or Constants.GreaterThan
							or Constants.EqualTo
							or Constants.LessThanOrEqual
							or Constants.GreaterThanOrEqual
					)
					{
						string value = (argument.Value.Value as string ?? throw new InvalidOperationException("Version condition must be a string."));
						if (!StructVersion.TryParse(value, out StructVersion ver) || ver.Tag != null)
						{
							Microsoft.CodeAnalysis.Text.TextSpan span = attribute.ApplicationSyntaxReference.Span;
							Location location = attribute.ApplicationSyntaxReference.SyntaxTree.GetLocation(span);
							Diagnostic diagnostic = Diagnostic.Create(Descriptor, location);
							context.ReportDiagnostic(diagnostic);
						}
					}
				}
			}
		}
	}
}
