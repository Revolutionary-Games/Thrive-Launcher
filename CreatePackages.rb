#!/usr/bin/env ruby

#
# Script that runs electron-packager with correct ignore folders
#

require 'optparse'

def getIgnoreFlags()

  return "--overwrite " +
    "--ignore=staging --ignore=installed --ignore=test " +
    # All the zips
    "--ignore=thrive-launcher-darwin-x64.7z " +
    "--ignore=thrive-launcher-linux-armv7l.7z " +
    "--ignore=thrive-launcher-linux-ia32.7z " +
    "--ignore=thrive-launcher-linux-x64.7z " +
    "--ignore=thrive-launcher-mas-x64.7z " +
    "--ignore=thrive-launcher-win32-ia32.7z " +
    "--ignore=thrive-launcher-win32-x64.7z "
    
end

def runBuildSingle(platform, arch)

  abort("no platform and no arch") if !platform && !arch

  # Fix arch
  if arch == '64'
    arch = 'x64'
  end
  if arch == '86' || arch == '32' || arch == 'x86'
    arch = 'ia32'
  end

  if platform

    if arch
      system("electron-packager ./ --platform=#{platform} --arch=#{arch} #{getIgnoreFlags}")
    else
      system("electron-packager ./ --platform=#{platform} #{getIgnoreFlags}")
    end
    
  else

    system("electron-packager ./ --arch=#{arch} #{getIgnoreFlags}")
    
  end
  
  abort("packaging failed") if $?.exitstatus != 0
  
end

# Zips up all created folders (uses regex to find them)
def zipThemUp()

  puts "Zipping up releases"

  files = Dir['*'].select {|x| x =~ /.*thrive-launcher-.*-(x64|ia32)(?!.7z)/i }

  files.each{|file|

    puts "Zipping '#{file}'"

    # TODO: version numbers added to these
    system("7za a #{file}.7z #{file}")
    abort("zipping failed") if $?.exitstatus != 0
  }
  
end

options = {}
OptionParser.new do |opts|
  opts.banner = "Usage: CreatePackages.rb --default"

  opts.on("-a", "--all", "Package all platforms") do |a|
    options[:all] = a
  end

  opts.on("-p", "--platforms linux,win32,...", Array, "Example 'list' of arguments") do |list|
    options[:platform] = list
  end

  opts.on("--no-zip", "Skips zipping") do |b|
    options[:noZip] = true
  end
  
  opts.on("-b", "--arch x86,x64", Array, "List of target architectures") do |list|
    options[:arch] = list
  end

  opts.on("-d", "--default", "Builds for all architectures that make sense for thrive") do |d|
    # Basically the same as --platform win32,linux --arch x86,x64, but no 32 bit linux
    options[:default] = d
  end
  
end.parse!

if !ARGV.empty?

  abort("Unkown arguments. See --help. This was left unparsed: " + ARGV.join(' '))
  
end

SKIP_ZIP = options[:noZip]

def doZip()

  if SKIP_ZIP
    return
  end

  zipThemUp
  
end

if options[:all]

  system("electron-packager ./ --all #{getIgnoreFlags}")
  
  abort("packaging failed") if $?.exitstatus != 0
  doZip
  exit
  
end

if options[:default]

  runBuildSingle("linux", "x64")
  runBuildSingle("win32", "x64")
  runBuildSingle("win32", "x86")

  doZip
  exit
  
end

# Run default if nothing selected
if !options[:platform] && !options[:arch]

  puts "Running only for current platform"

  system("electron-packager ./ #{getIgnoreFlags}")
  
  abort("packaging failed") if $?.exitstatus != 0
  doZip
  exit
  
end

if !options[:platform]

  # Run only with arch
  options[:arch].each{|a|

    runBuildSingle(nil, a)
  }

  doZip
  exit
  
end

# run all combinations
options[:platform].each{|p|

  if options[:arch]
    options[:arch].each{|a|

      runBuildSingle(p, a)
    }
  else
    runBuildSingle(p, nil)
  end
}

doZip










