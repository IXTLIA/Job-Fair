"""Original portable seed core shared with ffv_runtime/SeedLogic.cs.

This does not alter the existing static startup compiler or its Random-based
sampling. Runtime seed selection uses an explicitly versioned SHA-256 ranking.
"""
from __future__ import annotations

import hashlib
import secrets
from collections.abc import Iterable

ALGORITHM_VERSION = "starter-jobs-sha256-v1"
UINT64_MAX = (1 << 64) - 1
INT32_MAX = (1 << 31) - 1
# Match System.String.Trim / Char.IsWhiteSpace instead of Python's additional
# C0 information-separator characters U+001C through U+001F.
_TRIM_WHITESPACE = "\t\n\v\f\r \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000"


def try_parse_seed(text: str | None) -> tuple[bool, int]:
    if not isinstance(text, str):
        return False, 0
    value = text.strip(_TRIM_WHITESPACE)
    if not value:
        return False, 0
    parsed = 0
    for character in value:
        if character < "0" or character > "9":
            return False, 0
        digit = ord(character) - ord("0")
        if parsed > (UINT64_MAX - digit) // 10:
            return False, 0
        parsed = parsed * 10 + digit
    return True, parsed


def canonical_seed(seed: int) -> str:
    if type(seed) is not int or not 0 <= seed <= UINT64_MAX:
        raise ValueError("Seed must be an unsigned 64-bit integer")
    return str(seed)


def generate_seed() -> int:
    return int.from_bytes(secrets.token_bytes(8), byteorder="little", signed=False)


def select_starting_jobs(seed: int, job_ids: Iterable[int], count: int = 4) -> list[int]:
    seed_text = canonical_seed(seed)
    if type(count) is not int or count < 1:
        raise ValueError("Count must be a positive integer")
    ids = list(job_ids)
    if any(type(job) is not int or not 1 <= job <= INT32_MAX for job in ids):
        raise ValueError("Job IDs must be positive signed 32-bit integers")
    if len(set(ids)) != len(ids):
        raise ValueError("Duplicate job ID")
    if count > len(ids):
        raise ValueError("Count exceeds available jobs")
    ranked = [(hashlib.sha256(f"ProjectBlues|starter-jobs|1|{seed_text}|{job}".encode("utf-8")).digest(), job)
              for job in sorted(ids)]
    return [job for _, job in sorted(ranked)[:count]]
