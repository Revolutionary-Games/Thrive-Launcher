#!/usr/bin/env ruby
# Creates all the required icon files from the source files

require 'fileutils'

TARGET_PATH = "assets/icons/"
SOURCE_IMAGE = "assets/launcher-icon.png"

FileUtils.mkdir_p TARGET_PATH

abort("icon source doesn't exist") if !File.exists?(SOURCE_IMAGE)


def createSingleImage(size, ext)

  system("convert #{SOURCE_IMAGE} -resize #{size}x#{size} #{File.join(TARGET_PATH,
        size.to_s + 'x' + size.to_s + '.' + ext)}")

  abort("converting failed") if $?.exitstatus != 0
  
end

# Method from:
# http://stackoverflow.com/questions/11423711/recipe-for-creating-windows-ico-files-with-imagemagick
def createMultiSize(ext)

  # system("convert #{SOURCE_IMAGE} -bordercolor white -border 0 " + 
  #        "\\( -clone 0 -resize 16x16 \\) " +
  #        "\\( -clone 0 -resize 32x32 \\) " +
  #        "\\( -clone 0 -resize 48x48 \\) " +
  #        "\\( -clone 0 -resize 64x64 \\) " +
  #        "-delete 0 -alpha off -colors 256 #{File.join(TARGET_PATH, 'icon.' + ext)}")

  # This version keeps transparency
  system("convert #{SOURCE_IMAGE} " + 
         "\\( -clone 0 -resize 16x16 \\) " +
         "\\( -clone 0 -resize 32x32 \\) " +
         "\\( -clone 0 -resize 48x48 \\) " +
         "\\( -clone 0 -resize 64x64 \\) " +
         "-delete 0 #{File.join(TARGET_PATH, 'icon.' + ext)}")
  
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
system("convert " + [16, 32, 64, 128, 256].reverse().map{|a| File.join(TARGET_PATH,
                a.to_s + 'x' + a.to_s + '.png')}.join(' ') + " " +
       File.join(TARGET_PATH, 'icon.icns'))

# And neither does this
#createMultiSize("icns")

# looks like the only solution is to use this:
# https://github.com/pornel/libicns

# Windows icon
createMultiSize("ico")








