using System;

// Всплывающие сообщения в углу экрана ("+1 Сигареты"). Кто угодно постит, GameHUD показывает.
public static class HudMessages
{
    public static event Action<string> Posted;

    public static void Post(string text) => Posted?.Invoke(text);
}
