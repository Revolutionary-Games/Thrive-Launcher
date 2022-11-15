FROM fedora:36

RUN dnf install -y --setopt=deltarpm=false git git-lfs dotnet-sdk-6.0 p7zip ImageMagick && dnf clean all
RUN git lfs install
