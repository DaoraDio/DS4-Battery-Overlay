using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;

public sealed class ManagedGraphicsReferences : MarshalByRefObject
{
    internal static GraphicsApi Read(string executable)
    {
        AppDomain inspection = null;
        try
        {
            inspection = AppDomain.CreateDomain("DS4Overlay.GraphicsReferences");
            var reader = (ManagedGraphicsReferences)inspection.CreateInstanceFromAndUnwrap(
                typeof(ManagedGraphicsReferences).Assembly.Location, typeof(ManagedGraphicsReferences).FullName);
            return reader.UsesXnaGraphics(executable) ? GraphicsApi.Direct3D9 : GraphicsApi.None;
        }
        catch (IOException) { return GraphicsApi.None; }
        catch (UnauthorizedAccessException) { return GraphicsApi.None; }
        catch (BadImageFormatException) { return GraphicsApi.None; }
        catch (SecurityException) { return GraphicsApi.None; }
        finally
        {
            // Unloading releases the metadata and file handles after each inspection.
            if (inspection != null) AppDomain.Unload(inspection);
        }
    }

    public bool UsesXnaGraphics(string executable)
    {
        return Assembly.ReflectionOnlyLoadFrom(executable).GetReferencedAssemblies()
            .Any(reference => reference.Name == "Microsoft.Xna.Framework.Graphics");
    }
}
