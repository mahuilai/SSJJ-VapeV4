/* ====================================================================
 * encrypt_tool.c - build-time encryptor for L3.
 *
 *   ssjj_encrypt.exe <infile> <outfile>
 *
 * Reads <infile>, XORs it with the keyed keystream, writes <outfile>.
 * Because XOR is symmetric, the same tool can decrypt for testing.
 * ==================================================================== */
#include "ssjj_crypto.h"

#include <stdio.h>
#include <stdlib.h>

int main(int argc, char **argv) {
    FILE *input;
    FILE *output;
    long length;
    unsigned char *buffer;
    size_t read_bytes;
    size_t written;

    if (argc != 3) {
        fprintf(stderr, "usage: ssjj_encrypt <infile> <outfile>\n");
        return 1;
    }

    input = fopen(argv[1], "rb");
    if (input == NULL) {
        fprintf(stderr, "cannot open input: %s\n", argv[1]);
        return 1;
    }
    if (fseek(input, 0, SEEK_END) != 0) {
        fclose(input);
        return 1;
    }
    length = ftell(input);
    if (length <= 0) {
        fclose(input);
        return 1;
    }
    fseek(input, 0, SEEK_SET);

    buffer = (unsigned char *)malloc((size_t)length);
    if (buffer == NULL) {
        fclose(input);
        return 1;
    }
    read_bytes = fread(buffer, 1, (size_t)length, input);
    fclose(input);
    if (read_bytes != (size_t)length) {
        free(buffer);
        return 1;
    }

    ssjj_crypto_init();
    ssjj_crypto_xor(buffer, (size_t)length);

    output = fopen(argv[2], "wb");
    if (output == NULL) {
        free(buffer);
        fprintf(stderr, "cannot open output: %s\n", argv[2]);
        return 1;
    }
    written = fwrite(buffer, 1, (size_t)length, output);
    fclose(output);
    free(buffer);

    if (written != (size_t)length) {
        return 1;
    }
    return 0;
}
