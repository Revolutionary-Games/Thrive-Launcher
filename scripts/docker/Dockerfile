FROM fedora:34

RUN dnf install -y --setopt=deltarpm=false git git-lfs nodejs p7zip && dnf clean all
RUN git lfs install
