using System;

namespace Amcache.Classes;

public class Shortcut
{
    public Shortcut(string keyName, string lnkName, DateTimeOffset keyLastWriteTimestamp)
    {
        KeyName = keyName;
        KeyLastWriteTimestamp = keyLastWriteTimestamp;
        LnkName = lnkName;
    }

    public string KeyName { get; }
    public string LnkName { get; }

    public DateTimeOffset KeyLastWriteTimestamp { get; }

    /// <summary>The shortcut's target (databases; hives from before Windows 11 26H1 only have the shortcut's path)</summary>
    public string ShortcutTargetPath { get; set; } = string.Empty;

    /// <summary>The application user model id of the target</summary>
    public string ShortcutAumid { get; set; } = string.Empty;

    /// <summary>The ProgramId of the target's program (see the program entries)</summary>
    public string ShortcutProgramId { get; set; } = string.Empty;
}