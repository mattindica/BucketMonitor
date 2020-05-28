# Containerization
`Container.targets` and `Container.props` as well as their platform specific extensions
contain the necessary MSBuild automation to create container images of the project
automatically after `dotnet publish ...`

The recommended command line for publishing this application is 

```bash
dotnet publish -c Release -r linux-musl-x64 --self-contained
```

By default the container is tagged with the commit hash it was built against.
This is discovered using `git describe --dirty --always`

The container image can also be built manually with a command like the following
```bash
docker build \
-t indicalabs/bucketmonitor:`git describe --dirty --always` \
-f Linux.Dockerfile \
bin/Release/netcoreapp3.1/publish
```
_The context path here is important, only the binaries are desired in the container_

_Adjust accordingly if necessary_

## Custom behavior
The following properties can be set in the `dotnet publish` command to control
advanced behavior of the container building process. Set them on the
command line like so `-p:{PROPERTY_NAME}={VALUE}`

* `Containerize`: (`true/false`): Attempts to produce a container when `true`
* `PushContainer`: (`true/false`): Attempts to `docker push...` once build is complete.
* `ContainerRegistry`: Destination registry (default: `indicalabs/`) Note: Trailing slash is required here.
* `ContainerTag`: Used to override the auto generated commit hash tag.
* `LinuxContainerBase`: The base image to use
(default: Windows: `mcr.microsoft.com/windows/nanoserver`, Linux: `alpine`)
* `LinuxContainerTag`: This is separate from `{Windows,Linux}ContainerBase` so that it can be appended to the resulting container
(default: Windows: `1809`, Linux: `3`)
* `IncludeBaseTagInContainerId`: (`true`, `false`) When `true` includes the tag of the base container in the final container tag
* `LinuxMicroserviceDockerfilePath`: If a specific Dockerfile implementation is required for a particular platform it can be specified here.



# Using the container

To run the container simply 

```bash
docker run -d \
--restart=unless-stopped \
--name bucketmonitor \
-v images:/mnt/bucketmonitor \
indicalabs/linux/bucketmonitor
```

The default entrypoint executes `./BucketMonitor monitor` but can be modified using the `--entrypoint` option