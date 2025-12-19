using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MidiBard.Util;

public static class ArgumentParser
{
    public static List<string> ParseChatArgs(string args)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(args))
            return list;

        args = args.Trim();

        // split first token subcommand
        var match = Regex.Match(args, @"^(\S+)(?:\s+(.*))?$", RegexOptions.Singleline);
        if (!match.Success)
            return list;

        var command = match.Groups[1].Value;
        var remainder = match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;

        list.Add(command);

        if (string.IsNullOrEmpty(remainder))
            return list;

        // remove surrounding quotes
        if (remainder.Length >= 2 &&
            remainder.StartsWith("\"") &&
            remainder.EndsWith("\""))
        {
            remainder = remainder.Substring(1, remainder.Length - 2);
        }

        list.Add(remainder);
        return list;
    }
}
