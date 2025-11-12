using System;

namespace SmartStudyFunc.Utils
{
    /// <summary>
    /// Utility class for embedding vector mathematics
    /// </summary>
    public static class EmbeddingMath
    {
        /// <summary>
        /// Converts a byte array (stored in database) to a float array for vector calculations
        /// </summary>
        /// <param name="bytes">Byte array representation of float vector</param>
        /// <returns>Float array</returns>
        public static float[] BytesToFloatArray(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException("Byte array cannot be null or empty", nameof(bytes));
            }

            if (bytes.Length % sizeof(float) != 0)
            {
                throw new ArgumentException("Byte array length must be a multiple of 4 (float size)", nameof(bytes));
            }

            int floatCount = bytes.Length / sizeof(float);
            float[] floats = new float[floatCount];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            return floats;
        }

        /// <summary>
        /// Converts a float array to byte array for database storage
        /// </summary>
        /// <param name="floats">Float array</param>
        /// <returns>Byte array representation</returns>
        public static byte[] FloatArrayToBytes(float[] floats)
        {
            if (floats == null || floats.Length == 0)
            {
                throw new ArgumentException("Float array cannot be null or empty", nameof(floats));
            }

            byte[] bytes = new byte[floats.Length * sizeof(float)];
            Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>
        /// Computes cosine similarity between two vectors
        /// Returns a value between -1 and 1, where 1 means identical direction
        /// </summary>
        /// <param name="vector1">First vector</param>
        /// <param name="vector2">Second vector</param>
        /// <returns>Cosine similarity score</returns>
        public static double CosineSimilarity(float[] vector1, float[] vector2)
        {
            if (vector1 == null || vector2 == null)
            {
                throw new ArgumentNullException("Vectors cannot be null");
            }

            if (vector1.Length != vector2.Length)
            {
                throw new ArgumentException($"Vectors must have same dimension. Got {vector1.Length} and {vector2.Length}");
            }

            if (vector1.Length == 0)
            {
                throw new ArgumentException("Vectors cannot be empty");
            }

            double dotProduct = 0.0;
            double magnitude1 = 0.0;
            double magnitude2 = 0.0;

            for (int i = 0; i < vector1.Length; i++)
            {
                dotProduct += vector1[i] * vector2[i];
                magnitude1 += vector1[i] * vector1[i];
                magnitude2 += vector2[i] * vector2[i];
            }

            magnitude1 = Math.Sqrt(magnitude1);
            magnitude2 = Math.Sqrt(magnitude2);

            if (magnitude1 == 0.0 || magnitude2 == 0.0)
            {
                return 0.0;
            }

            return dotProduct / (magnitude1 * magnitude2);
        }

        /// <summary>
        /// Computes cosine similarity between two byte array embeddings
        /// Convenience method that handles conversion automatically
        /// </summary>
        /// <param name="embedding1">First embedding as byte array</param>
        /// <param name="embedding2">Second embedding as byte array</param>
        /// <returns>Cosine similarity score</returns>
        public static double CosineSimilarity(byte[] embedding1, byte[] embedding2)
        {
            float[] vector1 = BytesToFloatArray(embedding1);
            float[] vector2 = BytesToFloatArray(embedding2);
            return CosineSimilarity(vector1, vector2);
        }
    }
}
