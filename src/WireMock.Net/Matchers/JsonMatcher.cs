using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Stef.Validation;
using WireMock.Util;
using JsonUtils = WireMock.Util.JsonUtils;

namespace WireMock.Matchers;

/// <summary>
/// JsonMatcher
/// </summary>
public class JsonMatcher : IJsonMatcher
{
    /// <inheritdoc />
    public virtual string Name => nameof(JsonMatcher);

    /// <inheritdoc />
    public object Value { get; }

    /// <inheritdoc />
    public MatchBehaviour MatchBehaviour { get; }

    /// <inheritdoc cref="IIgnoreCaseMatcher.IgnoreCase"/>
    public bool IgnoreCase { get; }

    /// <summary>
    /// Support Regex
    /// </summary>
    public bool Regex { get; }

    private readonly JToken _valueAsJToken;
    private readonly Func<JToken, JToken> _jTokenConverter;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonMatcher"/> class.
    /// </summary>
    /// <param name="value">The string value to check for equality.</param>
    /// <param name="ignoreCase">Ignore the case from the PropertyName and PropertyValue (string only).</param>
    /// <param name="regex">Support Regex.</param>
    public JsonMatcher(string value, bool ignoreCase = false, bool regex = false) : this(MatchBehaviour.AcceptOnMatch, value, ignoreCase, regex)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonMatcher"/> class.
    /// </summary>
    /// <param name="value">The object value to check for equality.</param>
    /// <param name="ignoreCase">Ignore the case from the PropertyName and PropertyValue (string only).</param>
    /// <param name="regex">Support Regex.</param>
    public JsonMatcher(object value, bool ignoreCase = false, bool regex = false) : this(MatchBehaviour.AcceptOnMatch, value, ignoreCase, regex)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonMatcher"/> class.
    /// </summary>
    /// <param name="matchBehaviour">The match behaviour.</param>
    /// <param name="value">The value to check for equality.</param>
    /// <param name="ignoreCase">Ignore the case from the PropertyName and PropertyValue (string only).</param>
    /// <param name="regex">Support Regex.</param>
    public JsonMatcher(MatchBehaviour matchBehaviour, object value, bool ignoreCase = false, bool regex = false)
    {
        Guard.NotNull(value);

        MatchBehaviour = matchBehaviour;
        IgnoreCase = ignoreCase;
        Regex = regex;

        Value = value;
        _valueAsJToken = JsonUtils.ConvertValueToJToken(value);
        _jTokenConverter = ignoreCase ? Rename : jToken => jToken;
    }

    /// <inheritdoc />
    public MatchResult IsMatch(object? input)
    {
        var score = MatchScores.Mismatch;
        Exception? error = null;

        // When input is null or byte[], return Mismatch.
        if (input != null && input is not byte[])
        {
            try
            {
                var inputAsJToken = JsonUtils.ConvertValueToJToken(input);

                var match = IsMatch(_jTokenConverter(_valueAsJToken), _jTokenConverter(inputAsJToken));
                score = MatchScores.ToScore(match);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }

        return new MatchResult(MatchBehaviourHelper.Convert(MatchBehaviour, score), error);
    }

    /// <summary>
    /// Compares the input against the matcher value
    /// </summary>
    /// <param name="value">Matcher value</param>
    /// <param name="input">Input value</param>
    /// <returns></returns>
    protected virtual bool IsMatch(JToken value, JToken? input)
    {
        // If equal, return true.
        if (input == value)
        {
            return true;
        }

        // If input is null, return false.
        if (input == null)
        {
            return false;
        }

        // If using Regex and the value is a string, use the MatchRegex method.
        if (Regex && value.Type == JTokenType.String)
        {
            var valueAsString = value.ToString();

            var (valid, result) = RegexUtils.MatchRegex(valueAsString, input.ToString());
            if (valid)
            {
                return result;
            }
        }

        // If the value is a Guid and the input is a string, or vice versa, convert them to strings and compare the string values.
        if ((value.Type == JTokenType.Guid && input.Type == JTokenType.String) || (value.Type == JTokenType.String && input.Type == JTokenType.Guid))
        {
            return JToken.DeepEquals(value.ToString().ToUpperInvariant(), input.ToString().ToUpperInvariant());
        }

        switch (value.Type)
        {
            // If the value is an object, compare all properties.
            case JTokenType.Object:
                var valueProperties = value.ToObject<Dictionary<string, JToken>>() ?? new Dictionary<string, JToken>();
                var inputProperties = input.ToObject<Dictionary<string, JToken>>() ?? new Dictionary<string, JToken>();

                // If the number of properties is different, return false.
                if (valueProperties.Count != inputProperties.Count)
                {
                    return false;
                }

                // Compare all properties. The input must match all properties of the value.
                foreach (var pair in valueProperties)
                {
                    if (!IsMatch(pair.Value, inputProperties[pair.Key]))
                    {
                        return false;
                    }
                }

                return true;

            // If the value is an array, compare all elements.
            case JTokenType.Array:
                var valueArray = value.ToObject<JToken[]>() ?? EmptyArray<JToken>.Value;
                var inputArray = input.ToObject<JToken[]>() ?? EmptyArray<JToken>.Value;

                // If the number of elements is different, return false.
                if (valueArray.Length != inputArray.Length)
                {
                    return false;
                }

                return !valueArray.Where((valueToken, index) => !IsMatch(valueToken, inputArray[index])).Any();

            default:
                // Use JToken.DeepEquals() for all other types.
                return JToken.DeepEquals(value, input);
        }
    }

    private static string? ToUpper(string? input)
    {
        return input?.ToUpperInvariant();
    }

    // https://stackoverflow.com/questions/11679804/json-net-rename-properties
    private static JToken Rename(JToken json)
    {
        if (json is JProperty property)
        {
            JToken propertyValue = property.Value;
            if (propertyValue.Type == JTokenType.String)
            {
                string stringValue = propertyValue.Value<string>()!;
                propertyValue = ToUpper(stringValue);
            }

            return new JProperty(ToUpper(property.Name)!, Rename(propertyValue));
        }

        if (json is JArray array)
        {
            var renamedValues = array.Select(Rename);
            return new JArray(renamedValues);
        }

        if (json is JObject obj)
        {
            var renamedProperties = obj.Properties().Select(Rename);
            return new JObject(renamedProperties);
        }

        return json;
    }
}