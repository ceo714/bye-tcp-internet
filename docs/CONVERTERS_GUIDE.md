# ✅ Конвертеры и стили для WinUI 3 приложения

## 📦 Созданные компоненты

### 1. Конвертеры значений

**Файл:** `src/ByeTcp.UI/Converters/CommonConverters.cs`

| Конвертер | Назначение | Пример использования |
|-----------|------------|---------------------|
| `BoolToColorConverter` | Boolean → Color (Green/Red) | Статус службы |
| `BoolToVisibilityConverter` | Boolean → Visibility | Показ/скрытие элементов |
| `BoolNegationToVisibilityConverter` | Inverted Boolean → Visibility | Обратная видимость |
| `BoolNegationConverter` | Invert Boolean | Инверсия флага |
| `NullToBoolConverter` | Null → Boolean | Проверка на null |
| `LossToColorConverter` | Double (loss %) → Color | Packet loss индикация |
| `LevelToColorConverter` | String (log level) → Color | Цвет логов |
| `QualityToColorConverter` | NetworkQuality → Color | Качество сети |
| `FormatStringConverter` | Value → Formatted String | Форматирование |
| `TimeSpanToStringConverter` | TimeSpan → String (hh:mm:ss) | Uptime отображение |
| `DateTimeToStringConverter` | DateTime → String | Формат даты |
| `DoubleToStringConverter` | Double → String (F2) | Формат чисел |

---

## 2. Карточка CardControl

**Файл:** `src/ByeTcp.UI/Controls/CardControl.cs`

```csharp
public sealed class CardControl : ContentControl
{
    public CardControl()
    {
        this.DefaultStyleKey = typeof(CardControl);
    }
}
```

**Стиль:** `src/ByeTcp.UI/Themes/Generic.xaml`

```xml
<Style TargetType="controls:CardControl">
    <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Padding" Value="16" />
</Style>
```

---

## 3. Регистрация в App.xaml

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            <ResourceDictionary Source="Themes/Generic.xaml" />
        </ResourceDictionary.MergedDictionaries>
        
        <!-- Converters -->
        <converters:BoolToColorConverter x:Key="BoolToColorConverter" />
        <converters:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />
        <converters:LossToColorConverter x:Key="LossToColorConverter" />
        <!-- и т.д. -->
    </ResourceDictionary>
</Application.Resources>
```

---

## 4. Примеры использования

### BoolToColorConverter

```xml
<FontIcon 
    Foreground="{Binding IsServiceRunning, Converter={StaticResource BoolToColorConverter}}"
    Glyph="&#xE74E;" />
```

### LossToColorConverter

```xml
<TextBlock 
    Text="{Binding PacketLossPercent, StringFormat={}{0:F2}}"
    Foreground="{Binding PacketLossPercent, Converter={StaticResource LossToColorConverter}}" />
```

### BoolToVisibilityConverter

```xml
<InfoBar
    IsOpen="{Binding ErrorMessage, Converter={StaticResource BoolToVisibilityConverter}}"
    Severity="Error"
    Message="{Binding ErrorMessage}" />
```

### CardControl

```xml
<controls:CardControl>
    <StackPanel Spacing="8">
        <TextBlock Text="Service Status" Style="{StaticResource SubtitleTextBlock}" />
        <!-- Content -->
    </StackPanel>
</controls:CardControl>
```

---

## 5. Обновленные файлы

| Файл | Изменения |
|------|-----------|
| `App.xaml` | ✅ Добавлены конвертеры и Generic.xaml |
| `DashboardPage.xaml` | ✅ Обновлён для использования CardControl |
| `Converters/CommonConverters.cs` | ✅ 12 конвертеров |
| `Controls/CardControl.cs` | ✅ Custom control |
| `Themes/Generic.xaml` | ✅ Стили для CardControl |

---

## 6. Сборка и проверка

```powershell
cd d:\bye-tcp-internet

# Сборка
"C:\Program Files\dotnet\dotnet.exe" build src\ByeTcp.UI\ByeTcp.UI.csproj -c Release

# Проверка конвертеров
# Все конвертеры зарегистрированы в App.xaml и доступны в XAML
```

---

## 7. Расширение

### Добавление нового конвертера

1. Создать класс в `Converters/`:

```csharp
public class MyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Logic here
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
```

2. Зарегистрировать в `App.xaml`:

```xml
<converters:MyConverter x:Key="MyConverter" />
```

3. Использовать в XAML:

```xml
<TextBlock Text="{Binding Value, Converter={StaticResource MyConverter}}" />
```

---

## ✅ Готово

Все конвертеры и стили созданы и зарегистрированы. Приложение готово к использованию!
