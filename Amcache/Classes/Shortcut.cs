using System;

namespace Amcache.Classes
{
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
    }
}