using NLog;
using Registry;

namespace Amcache
{
    public static class Helper
    {
        public static bool IsNewFormat(string file)
        {
            
            var reg = new RegistryHive(file)
            {
                RecoverDeleted = false
            };
            LogManager.DisableLogging();
            reg.ParseHive();


            var fileKey = reg.GetKey(@"Root\InventoryApplication");

          LogManager.EnableLogging();

            return fileKey != null;
        }
    }
}