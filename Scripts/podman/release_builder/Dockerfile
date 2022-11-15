FROM ubuntu:20.04

ARG DEBIAN_FRONTEND=noninteractive
ENV TZ=Etc/UTC

# This is downloaded by ../ContainerTool.cs so that needs to be updated if the base image above is updated
COPY packages-microsoft-prod.deb /packages-microsoft-prod.deb

RUN apt-get update && apt-get install -y ca-certificates && dpkg -i /packages-microsoft-prod.deb && \
    apt-get update && apt-get install -y dotnet-sdk-6.0 p7zip git rsync imagemagick && apt-get clean
