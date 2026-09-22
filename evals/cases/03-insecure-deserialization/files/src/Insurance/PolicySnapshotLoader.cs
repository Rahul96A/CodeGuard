using System.Runtime.Serialization.Formatters.Binary;

namespace Insurance;

public sealed class PolicySnapshotLoader
{
    public object Load(Stream uploadedFile)
    {
#pragma warning disable SYSLIB0011
        var formatter = new BinaryFormatter();
        return formatter.Deserialize(uploadedFile);
#pragma warning restore SYSLIB0011
    }
}
