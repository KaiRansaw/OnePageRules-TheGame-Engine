using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FDG.Rules.Definitions;

namespace FDG.Rules.Serialization;

/// <summary>
/// Reads a bare JSON number as <see cref="ValueSource.Literal"/>, so a saved file written when an
/// effect's value was a plain <c>int</c> still loads once that value becomes a
/// <see cref="ValueSource"/>.
/// <para>
/// #412, and it is a compatibility fix rather than a convenience. Turning
/// <c>Effect.ExtraAttack.Attacks</c> into a ValueSource (OPR 3.6.0 rated Takedown Strike / Takedown
/// Shot, so "one attack" became "X attacks") made every previously saved army that EMBEDDED one of
/// those definitions - which is every army that fielded a carrier - fail to deserialise outright, with
/// a JsonException rather than a dropped rule. A player's saved lists are not regenerable data, so the
/// old shape has to keep loading, not merely degrade.
/// </para>
/// <para>
/// The "kind" dispatch is written out by hand rather than delegated back to STJ's polymorphic reader.
/// It has to be: a plain <c>JsonConverter&lt;ValueSource&gt;</c> over a <c>[JsonPolymorphic]</c> base
/// throws "the converter for derived type does not support metadata writes or reads" the moment the
/// base appears nested inside another polymorphic type, which <see cref="Effect"/> always is. So this
/// converter owns the whole mapping, and <c>ValueSourceJsonConverterTests</c> reflects over
/// <see cref="JsonDerivedTypeAttribute"/> to prove the mapping still covers every declared kind - add a
/// ValueSource subtype without touching this file and that test fails rather than the next save.
/// </para>
/// <para>
/// Writing always emits the tagged object form, so a file written today comes back through the "kind"
/// path and the bare-number branch only ever sees genuinely old input.
/// </para>
/// </summary>
public sealed class ValueSourceJsonConverter : JsonConverter<ValueSource>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ValueSource);

    public override ValueSource? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        // The compatibility branch: "attacks": 1 from a file saved before this field became a
        // ValueSource. Tolerates 2.0 as well as 2, since an int that passed through a float at some
        // point in the past should not be a load failure now.
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.TryGetInt32(out int whole)
                ? new ValueSource.Literal(whole)
                : new ValueSource.Literal((int)Math.Round(reader.GetDouble()));
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                $"A ValueSource must be an object with a \"kind\", or a bare number; found {reader.TokenType}.");
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement element = document.RootElement;

        if (!element.TryGetProperty("kind", out JsonElement kindElement)
            || kindElement.GetString() is not { Length: > 0 } kind)
        {
            throw new JsonException("A ValueSource object must carry a \"kind\" discriminator.");
        }

        return FromElement(kind, element, options);
    }

    private static ValueSource FromElement(string kind, JsonElement element, JsonSerializerOptions options)
    {
        switch (kind)
        {
            case LiteralKind:
                return new ValueSource.Literal(IntProperty(element, "value", kind));

            case ArgKind:
                return new ValueSource.Arg(IntProperty(element, "index", kind));

            case RuleCarrierCountKind:
                return new ValueSource.RuleCarrierCount();

            case NegateKind:
            {
                if (!element.TryGetProperty("inner", out JsonElement inner))
                {
                    throw new JsonException($"A \"{kind}\" ValueSource must carry an \"inner\" value.");
                }

                ValueSource? nested = JsonSerializer.Deserialize<ValueSource>(inner.GetRawText(), options);
                if (nested == null) throw new JsonException($"A \"{kind}\" ValueSource's inner value is null.");
                return new ValueSource.Negate(nested);
            }

            default:
                throw new JsonException($"Unknown ValueSource kind \"{kind}\".");
        }
    }

    private static int IntProperty(JsonElement element, string name, string kind)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int result))
        {
            throw new JsonException($"A \"{kind}\" ValueSource must carry an integer \"{name}\".");
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, ValueSource value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case ValueSource.Literal literal:
                writer.WriteString("kind", LiteralKind);
                writer.WriteNumber("value", literal.Value);
                break;
            case ValueSource.Arg arg:
                writer.WriteString("kind", ArgKind);
                writer.WriteNumber("index", arg.Index);
                break;
            case ValueSource.RuleCarrierCount:
                writer.WriteString("kind", RuleCarrierCountKind);
                break;
            case ValueSource.Negate negate:
                writer.WriteString("kind", NegateKind);
                writer.WritePropertyName("inner");
                JsonSerializer.Serialize(writer, negate.Inner, options);
                break;
            default:
                throw new JsonException(
                    $"No ValueSource serialization for {value.GetType().Name} - add it here and to "
                    + "FromElement, which ValueSourceJsonConverterTests checks against the declared kinds.");
        }

        writer.WriteEndObject();
    }

    // The discriminators, exactly as the [JsonDerivedType] attributes on ValueSource declare them. Named
    // constants so the guard test can compare the two lists rather than re-typing the strings.
    public const string LiteralKind = "literal";
    public const string ArgKind = "arg";
    public const string RuleCarrierCountKind = "ruleCarrierCount";
    public const string NegateKind = "negate";

    /// <summary> The kinds this converter handles, for the drift guard. </summary>
    public static IReadOnlyCollection<string> HandledKinds { get; } =
        new[] { LiteralKind, ArgKind, RuleCarrierCountKind, NegateKind };

    /// <summary>
    /// The ValueSource subtypes that actually exist, for the drift guard - read off the type rather than
    /// off a [JsonDerivedType] list, because this converter replaced those attributes (see the remarks
    /// above). Names are camel-cased to the discriminators this converter writes, so the guard compares
    /// what the code DECLARES against what the converter HANDLES.
    /// </summary>
    public static IReadOnlyCollection<string> DeclaredKinds { get; } =
        typeof(ValueSource).GetNestedTypes()
            .Where(nested => nested.IsSealed && typeof(ValueSource).IsAssignableFrom(nested))
            .Select(nested => char.ToLowerInvariant(nested.Name[0]) + nested.Name[1..])
            .ToArray();
}
