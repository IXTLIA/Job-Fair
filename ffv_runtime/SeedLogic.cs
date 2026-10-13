// Original, portable starter-job seed algorithm. No Unity or game dependencies.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ProjectBlues.Runtime
{
    public static class SeedLogic
    {
        public const string AlgorithmVersion = "starter-jobs-sha256-v1";

        public static bool TryParseSeed(string text, out ulong seed)
        {
            seed = 0;
            if (text == null) return false;
            string value = text.Trim();
            if (value.Length == 0) return false;
            ulong parsed = 0;
            foreach (char character in value)
            {
                if (character < '0' || character > '9') return false;
                ulong digit = (ulong)(character - '0');
                if (parsed > (UInt64.MaxValue - digit) / 10) return false;
                parsed = parsed * 10 + digit;
            }
            seed = parsed;
            return true;
        }

        public static string CanonicalSeed(ulong seed)
        {
            return seed.ToString(CultureInfo.InvariantCulture);
        }

        public static ulong GenerateSeed()
        {
            byte[] bytes = new byte[8];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
                generator.GetBytes(bytes);
            ulong seed = 0;
            for (int index = 0; index < bytes.Length; index++)
                seed |= (ulong)bytes[index] << (8 * index);
            return seed;
        }

        public static int[] SelectStartingJobs(ulong seed, IEnumerable<int> jobIds, int count = 4)
        {
            if (jobIds == null) throw new ArgumentNullException(nameof(jobIds));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            var ids = new SortedSet<int>();
            foreach (int id in jobIds)
            {
                if (id <= 0) throw new ArgumentException("Job IDs must be positive.", nameof(jobIds));
                if (!ids.Add(id)) throw new ArgumentException("Duplicate job ID.", nameof(jobIds));
            }
            if (count > ids.Count) throw new ArgumentOutOfRangeException(nameof(count));
            string prefix = "ProjectBlues|starter-jobs|1|" + CanonicalSeed(seed) + "|";
            var ranked = new List<RankedJob>();
            using (SHA256 hash = SHA256.Create())
                foreach (int id in ids)
                    ranked.Add(new RankedJob(id, hash.ComputeHash(Encoding.UTF8.GetBytes(
                        prefix + id.ToString(CultureInfo.InvariantCulture)))));
            ranked.Sort(CompareRanks);
            int[] selected = new int[count];
            for (int index = 0; index < count; index++) selected[index] = ranked[index].Id;
            return selected;
        }

        private static int CompareRanks(RankedJob left, RankedJob right)
        {
            for (int index = 0; index < left.Digest.Length; index++)
            {
                int order = left.Digest[index].CompareTo(right.Digest[index]);
                if (order != 0) return order;
            }
            return left.Id.CompareTo(right.Id);
        }

        private sealed class RankedJob
        {
            public readonly int Id;
            public readonly byte[] Digest;
            public RankedJob(int id, byte[] digest) { Id = id; Digest = digest; }
        }
    }
}
