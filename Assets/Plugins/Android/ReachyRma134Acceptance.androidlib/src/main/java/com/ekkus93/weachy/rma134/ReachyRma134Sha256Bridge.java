package com.ekkus93.weachy.rma134;

import java.io.BufferedInputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;

/**
 * RMA-134 physical-acceptance-only artifact verifier.
 *
 * The full GGUF read and SHA-256 loop remains in Java so the 396 MB model bytes never
 * cross JNI. The caller receives only a 64-character lowercase digest. Progress is
 * diagnostic only and is never used as acceptance authority.
 */
public final class ReachyRma134Sha256Bridge {
    private static final int BUFFER_BYTES = 1024 * 1024;
    private static final long PROGRESS_INTERVAL_BYTES = 16L * 1024L * 1024L;
    private static final char[] HEX = "0123456789abcdef".toCharArray();

    private ReachyRma134Sha256Bridge() {
    }

    public static String sha256(String path, String progressPath)
            throws IOException, NoSuchAlgorithmException {
        if (path == null || path.isEmpty()) {
            throw new IllegalArgumentException("RMA-134 SHA path is empty.");
        }
        if (progressPath == null || progressPath.isEmpty()) {
            throw new IllegalArgumentException("RMA-134 SHA progress path is empty.");
        }

        File file = new File(path);
        if (!file.isFile()) {
            throw new IOException("RMA-134 staged model is not a regular file: " + path);
        }
        long totalBytes = file.length();
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        byte[] buffer = new byte[BUFFER_BYTES];
        long bytesRead = 0L;
        long nextProgress = PROGRESS_INTERVAL_BYTES;
        writeProgress(progressPath, bytesRead, totalBytes, false);

        try (BufferedInputStream input = new BufferedInputStream(
                new FileInputStream(file), BUFFER_BYTES)) {
            while (true) {
                int count = input.read(buffer);
                if (count < 0) {
                    break;
                }
                if (count == 0) {
                    continue;
                }
                digest.update(buffer, 0, count);
                bytesRead += count;
                if (bytesRead >= nextProgress) {
                    writeProgress(progressPath, bytesRead, totalBytes, false);
                    while (nextProgress <= bytesRead) {
                        nextProgress += PROGRESS_INTERVAL_BYTES;
                    }
                }
            }
        }

        if (bytesRead != totalBytes) {
            throw new IOException(
                    "RMA-134 SHA byte-count mismatch: expected " + totalBytes +
                    ", read " + bytesRead + ".");
        }
        writeProgress(progressPath, bytesRead, totalBytes, true);
        return toHex(digest.digest());
    }

    private static void writeProgress(
            String progressPath,
            long bytesRead,
            long totalBytes,
            boolean completed) throws IOException {
        String text =
                "bytes_read=" + bytesRead + "\n" +
                "total_bytes=" + totalBytes + "\n" +
                "completed=" + completed + "\n";
        try (FileOutputStream output = new FileOutputStream(progressPath, false)) {
            output.write(text.getBytes(StandardCharsets.UTF_8));
            output.flush();
            output.getFD().sync();
        }
    }

    private static String toHex(byte[] digest) {
        char[] output = new char[digest.length * 2];
        for (int index = 0; index < digest.length; ++index) {
            int value = digest[index] & 0xff;
            output[index * 2] = HEX[value >>> 4];
            output[index * 2 + 1] = HEX[value & 0x0f];
        }
        return new String(output);
    }
}
