using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace StoreOS.Api.Serialization;

public class FlexibleDateOnlyJsonConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return default;
            }

            if (DateOnly.TryParse(str, CultureInfo.InvariantCulture, out var dateOnly))
            {
                return dateOnly;
            }

            if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, out var dto))
            {
                return DateOnly.FromDateTime(dto.DateTime);
            }
        }

        throw new JsonException($"Unable to parse DateOnly from JSON token '{reader.TokenType}'.");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}

public class FlexibleNullableDateOnlyJsonConverter : JsonConverter<DateOnly?>
{
    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return null;
            }

            if (DateOnly.TryParse(str, CultureInfo.InvariantCulture, out var dateOnly))
            {
                return dateOnly;
            }

            if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, out var dto))
            {
                return DateOnly.FromDateTime(dto.DateTime);
            }
        }

        throw new JsonException($"Unable to parse DateOnly? from JSON token '{reader.TokenType}'.");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

public class DateOnlyModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        var value = valueProviderResult.FirstValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (bindingContext.ModelType == typeof(DateOnly?))
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            return Task.CompletedTask;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var dateOnly))
        {
            bindingContext.Result = ModelBindingResult.Success(dateOnly);
            return Task.CompletedTask;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, out var dto))
        {
            bindingContext.Result = ModelBindingResult.Success(DateOnly.FromDateTime(dto.DateTime));
            return Task.CompletedTask;
        }

        bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"The value '{value}' is not valid for DateOnly.");
        return Task.CompletedTask;
    }
}

public class DateOnlyModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.ModelType == typeof(DateOnly) || context.Metadata.ModelType == typeof(DateOnly?))
        {
            return new DateOnlyModelBinder();
        }

        return null;
    }
}
