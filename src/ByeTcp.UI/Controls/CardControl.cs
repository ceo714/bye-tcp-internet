using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ByeTcp.UI.Controls;

/// <summary>
/// Карточка-контейнер в стиле Fluent Design
/// </summary>
public sealed class CardControl : ContentControl
{
    public CardControl()
    {
        this.DefaultStyleKey = typeof(CardControl);
    }
}
