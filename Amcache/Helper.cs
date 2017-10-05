using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amcache.Classes;
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
            reg.ParseHive();

            var fileKey = reg.GetKey(@"Root\InventoryApplication");
            
            return fileKey != null;
        }
    }
}
