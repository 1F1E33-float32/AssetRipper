/*
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetRipper.Il2CppInspector.Reflection;

public static class Extensions
{
	// Convert a list of CustomAttributeData objects into C#-friendly attribute usages
	public static string ToString(
		this IEnumerable<CustomAttributeData> attributes,
		Scope? scope = null,
		string linePrefix = "",
		string attributePrefix = "",
		bool inline = false,
		bool emitPointer = false,
		bool mustCompile = false
	)
	{
		StringBuilder sb = new();
		foreach (CustomAttributeData cad in attributes)
		{
			if (cad.CtorInfo != null)
			{
				// v29+ attribute handling
				// We now have much more information, so we can reconstruct the actual attribute
				CustomAttributeCtor ctor = cad.CtorInfo;
				string? name = ctor.Ctor.DeclaringType.GetScopedCSharpName(scope);
				int suffix = name.LastIndexOf("Attribute", StringComparison.Ordinal);
				if (suffix != -1)
				{
					name = name[..suffix];
				}

				sb.Append(linePrefix);
				sb.Append('[');
				sb.Append(attributePrefix);
				sb.Append(name);
				int totalCount = ctor.Arguments.Length + ctor.Fields.Length + ctor.Properties.Length;
				if (totalCount > 0)
				{
					// We have parameters, need to use brackets
					sb.Append('(');
					int totalIndex = 0;
					foreach (CustomAttributeArgument argument in ctor.Arguments)
					{
						sb.Append(argument.Value.ToCSharpValue(argument.Type, scope));
						if (++totalIndex != totalCount)
						{
							sb.Append(", ");
						}
					}

					foreach (CustomAttributeFieldArgument field in ctor.Fields)
					{
						sb.Append(field.Field.CSharpName);
						sb.Append(" = ");
						sb.Append(field.Value.ToCSharpValue(field.Type, scope));
						if (++totalIndex != totalCount)
						{
							sb.Append(", ");
						}
					}

					foreach (CustomAttributePropertyArgument property in ctor.Properties)
					{
						sb.Append(property.Property.CSharpName);
						sb.Append(" = ");
						sb.Append(property.Value.ToCSharpValue(property.Type, scope));
						if (++totalIndex != totalCount)
						{
							sb.Append(", ");
						}
					}

					sb.Append(')');
				}

				sb.Append(']');
				sb.Append(inline ? " " : "\n");
			}
			else
			{
				// Pre-v29 attribute handling
				// Find a constructor that either has no parameters, or all optional parameters
				bool parameterlessConstructor = cad.AttributeType.DeclaredConstructors.Any(c =>
					!c.IsStatic && c.IsPublic && c.DeclaredParameters.All(p => p.IsOptional)
				);
				// IL2CPP doesn't retain attribute arguments so we have to comment out those with non-optional arguments if we want the output to compile
				string? commentStart =
					mustCompile && !parameterlessConstructor
						? inline
							? "/* "
							: "// "
						: "";
				string? commentEnd = commentStart.Length > 0 && inline ? " */" : "";
				string? arguments = "";
				// Set AttributeUsage(AttributeTargets.All) if making output that compiles to mitigate CS0592
				if (mustCompile && cad.AttributeType.FullName == "System.AttributeUsageAttribute")
				{
					commentStart = "";
					commentEnd = "";
					arguments = "(AttributeTargets.All)";
				}

				string? name = cad.AttributeType.GetScopedCSharpName(scope);
				int suffix = name.LastIndexOf("Attribute", StringComparison.Ordinal);
				if (suffix != -1)
				{
					name = name[..suffix];
				}

				sb.Append($"{linePrefix}{commentStart}[{attributePrefix}{name}{arguments}]{commentEnd}");
				if (emitPointer)
				{
					sb.Append($" {(inline ? "/*" : "//")} {cad.VirtualAddress.ToAddressString()}{(inline ? " */" : "")}");
				}

				sb.Append(inline ? " " : "\n");
			}
		}

		return sb.ToString();
	}

	// Output a ulong as a 32 or 64-bit hexadecimal address
	public static string ToAddressString(this ulong address) => address <= 0xffff_ffff ? $"0x{(uint)address:X8}" : $"0x{address:X16}";

	public static string ToAddressString(this long address) => ((ulong)address).ToAddressString();

	public static string ToAddressString(this (ulong start, ulong end)? address) =>
		ToAddressString(address?.start ?? 0) + "-" + ToAddressString(address?.end ?? 0);

	public static string ToAddressString(this (ulong start, ulong end) address) =>
		ToAddressString(address.start) + "-" + ToAddressString(address.end);

	// C# string literal escape characters
	// Taken from: https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/strings/#regular-and-verbatim-string-literals
	private static readonly Dictionary<char, string> EscapeChars = new()
	{
		['\''] = @"\'",
		['"'] = @"\""",
		['\\'] = @"\\",
		['\0'] = @"\0",
		['\a'] = @"\a",
		['\b'] = @"\b",
		['\f'] = @"\f",
		['\n'] = @"\n",
		['\r'] = @"\r",
		['\t'] = @"\t",
		['\v'] = @"\v",
	};

	// Output a string in Python-friendly syntax
	public static string ToEscapedString(this string str)
	{
		// Replace standard escape characters
		StringBuilder s = new();
		foreach (char chr in str)
		{
			if (EscapeChars.TryGetValue(chr, out string? escaped))
			{
				s.Append(escaped);
			}
			else if (chr < 32 || chr > 126)
			{
				s.Append("\\u");
				s.Append($"{(int)chr:X4}");
			}
			else
			{
				s.Append(chr);
			}
		}

		return s.ToString();
	}

	public static string ToCIdentifier(this string str, bool allowScopeQualifiers = false)
	{
		// replace * with Ptr
		str = str.Replace("*", "Ptr");
		// escape non-ASCII characters
		StringBuilder s = new();
		for (int i = 0; i < str.Length; i++)
		{
			if (str[i] < 32 || str[i] > 126)
			{
				s.Append($"u{(int)str[i]:X4}");
			}
			else
			{
				s.Append(str[i]);
			}
		}

		str = s.ToString();
		// replace illegal characters
		str = Regex.Replace(str, allowScopeQualifiers ? @"[^a-zA-Z0-9_\.:]" : "[^a-zA-Z0-9_]", "_");
		// ensure identifier starts with a letter or _ (and is non-empty)
		if (!Regex.IsMatch(str, "^[a-zA-Z_]"))
		{
			str = "_" + str;
		}

		return str;
	}

	// Output a value in C#-friendly syntax
	public static string ToCSharpValue(this object? value, TypeInfo type, Scope? usingScope = null)
	{
		switch (value)
		{
			case bool b:
				return b ? "true" : "false";
			case float f:
				return value switch
				{
					float.PositiveInfinity => "1F / 0F",
					float.NegativeInfinity => "-1F / 0F",
					float.NaN => "0F / 0F",
					_ => f.ToString(CultureInfo.InvariantCulture) + "f",
				};
			case double d:
				return value switch
				{
					double.PositiveInfinity => "1D / 0D",
					double.NegativeInfinity => "-1D / 0D",
					double.NaN => "0D / 0D",
					_ => d.ToString(CultureInfo.InvariantCulture),
				};
			case string str:
				return $"\"{str.ToEscapedString()}\"";
			case char c:
			{
				int cValue = (int)c;
				if (cValue < 32 || cValue > 126)
				{
					return $"'\\x{cValue:x4}'";
				}

				return $"'{value}'";
			}

			case TypeInfo typeInfo:
				return $"typeof({typeInfo.GetScopedCSharpName(usingScope)})";
			case CustomAttributeArgument[] array:
				StringBuilder arraySb = new();
				arraySb.Append("new ");
				arraySb.Append(type.GetScopedCSharpName(usingScope));
				arraySb.Append('[');
				arraySb.Append(array.Length);
				arraySb.Append(']');
				if (array.Length > 0)
				{
					arraySb.Append(" {");
					for (int i = 0; i < array.Length; i++)
					{
						arraySb.Append(array[i].Value.ToCSharpValue(array[i].Type, usingScope));
						if (i + 1 != array.Length)
						{
							arraySb.Append(", ");
						}
					}

					arraySb.Append(" }");
				}

				return arraySb.ToString();
		}

		if (type.IsEnum)
		{
			bool flags = type.GetCustomAttributes("System.FlagsAttribute").Any();
			Dictionary<string, object> values = type.GetEnumNames()
				.Zip(type.GetEnumValues().OfType<object>(), (k, v) => new { k, v })
				.ToDictionary(x => x.k, x => x.v);
			string typeName = type.GetScopedCSharpName(usingScope);
			// We don't know what type the enumeration or value is, so we use Object.Equals() to do content-based equality testing
			if (!flags)
			{
				// Defined enum name
				if (values.FirstOrDefault(v => v.Value.Equals(value)).Key is string enumValue)
				{
					return typeName + "." + enumValue;
				}

				// Undefined enum value (return a cast)
				return "(" + typeName + ") " + value;
			}

			// Logical OR a series of flags together
			// Values like 0x8000_0000_0000_0000 can't be cast to Int64
			// but values like 0xffff_ffff can't be cast to UInt64 (due to sign extension)
			// so we're just going to have to try to find a type that doesn't make it explode
			if (value is byte or ushort or uint or ulong)
			{
				ulong flagValue = Convert.ToUInt64(value);
				IEnumerable<string> setFlags = values
					.Where(x => (Convert.ToUInt64(x.Value) & flagValue) == Convert.ToUInt64(x.Value))
					.Select(x => typeName + "." + x.Key);
				return string.Join(" | ", setFlags);
			}
			else if (value is sbyte or short or int or long)
			{
				long flagValue = Convert.ToInt64(value);
				IEnumerable<string> setFlags = values
					.Where(x => (Convert.ToInt64(x.Value) & flagValue) == Convert.ToInt64(x.Value))
					.Select(x => typeName + "." + x.Key);
				return string.Join(" | ", setFlags);
			}
			else
			{
				throw new ArgumentException("Unsupported enum underlying type");
			}
		}

		// Structs and generic type parameters must use 'default' rather than 'null'
		return value?.ToString() ?? (type.IsValueType || type.IsGenericParameter ? "default" : "null");
	}
}
