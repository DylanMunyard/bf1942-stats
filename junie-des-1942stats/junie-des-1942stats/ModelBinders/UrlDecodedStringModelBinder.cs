using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace junie_des_1942stats.ModelBinders;

/// <summary>
/// Custom model binder that automatically URL-decodes string values.
/// This preserves + signs as spaces in URL-encoded strings.
/// </summary>
public class UrlDecodedStringModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext == null)
            throw new ArgumentNullException(nameof(bindingContext));

        var modelName = bindingContext.ModelName;
        var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);

        if (valueProviderResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(modelName, valueProviderResult);

        var value = valueProviderResult.FirstValue;

        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.CompletedTask;
        }

        try
        {
            var decodedValue = Uri.UnescapeDataString(value);
            bindingContext.Result = ModelBindingResult.Success(decodedValue);
        }
        catch (Exception ex)
        {
            bindingContext.ModelState.TryAddModelError(modelName, ex.Message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Model binder provider for UrlDecodedStringModelBinder.
/// </summary>
public class UrlDecodedStringModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (context.Metadata.ModelType == typeof(string))
        {
            return new UrlDecodedStringModelBinder();
        }

        return null;
    }
}
