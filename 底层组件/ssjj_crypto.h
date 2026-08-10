/* ====================================================================
 * ssjj_crypto.h - stream cipher for L3 (resource encryption).
 *
 * Simple keyed stream cipher (XOR with xorshift128+ keystream seeded
 * from the 32-byte SSJJ_KEY). Same routine encrypts at build time and
 * decrypts at runtime (XOR is symmetric).
 *
 * Security note: this is an obfuscation layer, not a DRM. The key is
 * inside the binary; L4 (VMProtect) hardens the code that uses it.
 * ==================================================================== */
#ifndef SSJJ_CRYPTO_H
#define SSJJ_CRYPTO_H

#include <stddef.h>
#include <stdint.h>

#include "ssjj_key.h"   /* generated: static const unsigned char SSJJ_KEY[32] */

static uint64_t ssjj_s0;
static uint64_t ssjj_s1;

static void ssjj_crypto_init(void) {
    uint64_t seed = 0x9E3779B97F4A7C15ULL;
    size_t i;
    for (i = 0; i < 32; ++i) {
        seed = (seed ^ (uint64_t)SSJJ_KEY[i])
                * 0x2545F4914F6CDD1DULL + 0x9E3779B97F4A7C15ULL;
    }
    ssjj_s0 = seed;
    ssjj_s1 = seed ^ 0x8DA08DA08DA08DA0ULL;
    if (ssjj_s0 == 0 && ssjj_s1 == 0) {
        ssjj_s0 = 0x9E3779B97F4A7C15ULL;
    }
}

static uint64_t ssjj_next_u64(void) {
    uint64_t x = ssjj_s0;
    uint64_t y = ssjj_s1;
    ssjj_s0 = y;
    x ^= x << 23;
    ssjj_s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
    return ssjj_s1 + y;
}

/* XOR buf in place with the keystream (encrypt == decrypt). */
static void ssjj_crypto_xor(unsigned char *buf, size_t len) {
    uint64_t k = 0;
    unsigned shift = 0; /* bytes of k already consumed */
    size_t i;
    for (i = 0; i < len; ++i) {
        if (shift == 0) {
            k = ssjj_next_u64();
            shift = 8;
        }
        buf[i] ^= (unsigned char)(k & 0xFF);
        k >>= 8;
        --shift;
    }
}

#endif /* SSJJ_CRYPTO_H */
