FROM almalinux:9

# This is downloaded by ../ContainerTool.cs so that needs to be updated if the base image above is updated
COPY packages-microsoft-prod.rpm /packages-microsoft-prod.rpm

RUN dnf update -y && dnf install epel-release -y && rpm -Uvh /packages-microsoft-prod.rpm  && \
    dnf install -y dotnet-sdk-9.0 p7zip git rsync ImageMagick && dnf clean all
