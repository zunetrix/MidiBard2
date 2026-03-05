namespace MidiBard;

internal static class ChatHelper
{
    public static string GetChatCommand(ChatType chatType)
    {
        return chatType switch
        {
            ChatType.Current => string.Empty,
            ChatType.Say => "/s ",
            ChatType.Party => "/p ",
            ChatType.Echo => "/echo ",
            ChatType.Yell => "/y ",
            _ => string.Empty
        };
    }
}
