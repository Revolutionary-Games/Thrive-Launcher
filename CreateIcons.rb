#!/usr/bin/env ruby
# Creates all the required icon files from the source files

require 'fileutils'
require 'os'

TARGET_PATH = "assets/icons/"
SOURCE_IMAGE = "assets/launcher-icon.png"

FileUtils.mkdir_p TARGET_PATH

abort("icon source doesn't exist") if !File.exists?(SOURCE_IMAGE)

# Windows uses a common magick.exe as the tool name instead of separate executables
# for all the tools like on Linux
def command
  if OS.windows?
    "magick.exe convert"
  else
    "convert"
  end
end


def createSingleImage(size, ext)

  system("#{command} #{SOURCE_IMAGE} -resize #{size}x#{size} #{File.join(TARGET_PATH,
        size.to_s + 'x' + size.to_s + '.' + ext)}")

  abort("converting failed") if $?.exitstatus != 0
  
end

# Method from:
# http://stackoverflow.com/questions/11423711/recipe-for-creating-windows-ico-files-with-imagemagick
def createMultiSize(ext)

  # This version keeps transparency
  system("#{command} #{SOURCE_IMAGE} " +
         [16, 32, 48, 64, 128, 256].map{|res|
           # Windows doesn't need escapes and doesn't support them
           if OS.windows?
             "( -clone 0 -resize #{res}x#{res} )"
           else
             "\\( -clone 0 -resize #{res}x#{res} \\)"
           end
         }.join(" ") +
         " -delete 0 -colors 256 #{File.join(TARGET_PATH, 'icon.' + ext)}")
end

# Wanted png sizes

createSingleImage(128, "png")
createSingleImage(64, "png")
createSingleImage(256, "png")

# Mac icon
# additional pngs
createSingleImage(16, "png")
createSingleImage(32, "png")

# Apparently this doesn't work for some reason
system("#{command} " + [16, 32, 64, 128, 256].reverse().map{|a| File.join(TARGET_PATH,
                a.to_s + 'x' + a.to_s + '.png')}.join(' ') + " " +
       File.join(TARGET_PATH, 'icon.icns'))

# And neither does this
#createMultiSize("icns")

# looks like the only solution is to use this:
# https://github.com/pornel/libicns

# Windows icon
createMultiSize("ico")








