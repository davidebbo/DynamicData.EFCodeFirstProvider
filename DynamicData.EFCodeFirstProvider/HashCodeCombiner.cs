using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DynamicData.EFCodeFirstProvider {
    static class HashCodeCombiner {
        internal static long CombineHashCodes(object o1, object o2) {
            // Start with a seed (obtained from String.GetHashCode implementation)
            long combinedHash = 5381;

            combinedHash = AddHashCode(combinedHash, o1);
            combinedHash = AddHashCode(combinedHash, o2);

            return combinedHash;
        }

        // Return a single hash code for 3 objects
        internal static long CombineHashCodes(object o1, object o2, object o3) {
            // Start with a seed (obtained from String.GetHashCode implementation)
            long combinedHash = 5381;

            combinedHash = AddHashCode(combinedHash, o1);
            combinedHash = AddHashCode(combinedHash, o2);
            combinedHash = AddHashCode(combinedHash, o3);

            return combinedHash;
        }

        private static long AddHashCode(long currentHash, object o) {
            if (o == null)
                return currentHash;

            return ((currentHash << 5) + currentHash) ^ o.GetHashCode();
        }
    }
}
