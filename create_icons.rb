#!/usr/bin/env ruby
# frozen_string_literal: true

# Creates all the required icon files from the source files

require 'English'
require 'fileutils'
require 'os'

TARGET_PATH = 'ThriveLauncher/Assets/Icons/'
TEMP_ICON_FOLDER = 'Temp/Icons'
SOURCE_IMAGE = 'ThriveLauncher/launcher-icon.png'

FileUtils.mkdir_p TARGET_PATH
FileUtils.mkdir_p TEMP_ICON_FOLDER

abort("icon source doesn't exist") unless File.exist?(SOURCE_IMAGE)

# Windows uses a common magick.exe as the tool name instead of separate executables
# for all the tools like on Linux
def command
  if OS.windows?
    ['magick.exe', 'convert']
  else
    ['convert']
  end
end

def create_single_image(size, ext, temp: true)
  target_folder = temp ? TEMP_ICON_FOLDER : TARGET_PATH

  system(*command, SOURCE_IMAGE, '-resize', "#{size}x#{size}",
         File.join(target_folder, "#{size}x#{size}.#{ext}"))

  abort('converting failed') if $CHILD_STATUS.exitstatus != 0
end

# Method from:
# http://stackoverflow.com/questions/11423711/recipe-for-creating-windows-ico-files-with-imagemagick
def create_multi_size(ext)
  # This version keeps transparency
  system(*command, SOURCE_IMAGE,
         *[16, 32, 48, 64, 128, 256].map do |res|
           ['(', '-clone', '0', '-resize', "#{res}x#{res}", ')']
         end.flatten, '-delete', '0', '-colors', '256', File.join(TARGET_PATH, "icon.#{ext}"))
end

# Wanted png sizes

create_single_image(128, 'png')
create_single_image(64, 'png')
create_single_image(256, 'png')
create_single_image(256, 'png', temp: false)

# Mac icon
# additional pngs
create_single_image(16, 'png')
create_single_image(32, 'png')

# Apparently this doesn't work for some reason
system(*command, *[16, 32, 64, 128, 256].reverse.map do |a|
                   File.join(TEMP_ICON_FOLDER, "#{a}x#{a}.png")
                 end, File.join(TARGET_PATH, 'icon.icns'))

# And neither does this
# create_multi_size("icns")

# looks like the only solution is to use this:
# https://github.com/pornel/libicns

# Windows icon
create_multi_size('ico')
