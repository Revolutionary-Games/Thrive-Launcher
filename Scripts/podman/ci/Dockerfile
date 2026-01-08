FROM fedora:43

RUN dnf install -y --setopt=deltarpm=false git git-lfs dotnet-sdk-10.0 p7zip ImageMagick && dnf clean all
RUN git lfs install
