namespace KasirPro.App.UI;

internal static class ControlExtensions
{
    public static T Tap<T>(this T item, Action<T> action)
    {
        action(item);
        return item;
    }
}
