#!/usr/bin/env ruby
# frozen_string_literal: true

require 'optparse'
require 'find'
require 'digest'
require 'nokogiri'
require 'set'

require_relative 'bootstrap_rubysetupsystem'
require_relative 'RevolutionaryGamesCommon/RubySetupSystem/RubyCommon'

MAX_LINE_LENGTH = 120

# VALID_CHECKS = %w[compile files inspectcode cleanupcode].freeze
VALID_CHECKS = %w[compile inspectcode cleanupcode].freeze
DEFAULT_CHECKS = VALID_CHECKS

JET_BRAINS_CACHE = '.jetbrains-cache'
ONLY_FILE_LIST = 'files_to_check.txt'

EXCLUDE_INSPECT_FILES = [
  '*.min.css',
  '*.dll'
].freeze

# Extra warnings to ignore in inspections that I couldn't figure out how to suppress normally
FORCE_IGNORED_INSPECTIONS = [].freeze

OUTPUT_MUTEX = Mutex.new
TOOL_RESTORE_MUTEX = Mutex.new
BUILD_MUTEX = Mutex.new

@options = {
  checks: DEFAULT_CHECKS,
  skip_file_types: [],
  parallel: true,
  restore_tools: true
}

OptionParser.new do |opts|
  opts.banner = "Usage: #{$PROGRAM_NAME} [options]"

  opts.on('-c', '--checks check1,check2', Array,
          'Select checks to do. Default is all') do |checks|
    @options[:checks] = checks
  end
  opts.on('-s', '--skip filetype1,filetype2', Array,
          'Skips files checks on the specified types') do |skip|
    @options[:skip_file_types] = skip
  end
  opts.on('-p', '--[no-]parallel', 'Run different checks in parallel (default)') do |b|
    @options[:parallel] = b
  end
  opts.on('-p', '--[no-]restore-tools',
          'Automatically restore dotnet tools or skip that') do |b|
    @options[:restore_tools] = b
  end
end.parse!

onError "Unhandled parameters: #{ARGV}" unless ARGV.empty?

info "Starting formatting checks with the following checks: #{@options[:checks]}"

# Helper functions

def ide_file?(path)
  path =~ %r{/\.vs/} || path =~ %r{/\.idea/}
end

def explicitly_ignored?(path)
  path =~ %r{/tmp/} || path =~ %r{/RubySetupSystem/}
end

def cache?(path)
  path =~ %r{/\bin/} || path =~ %r{/obj/} || path =~ %r{/\.git/}
end

# Skip some files that would otherwise be processed
def skip_file?(path)
  explicitly_ignored?(path) || path =~ %r{^\./\./} || cache?(path) || ide_file?(path)
end

def file_type_skipped?(path)
  if @options[:skip_file_types].include? File.extname(path)[1..-1]
    OUTPUT_MUTEX.synchronize do
      puts "Skipping file '#{path}'"
    end
    true
  else
    false
  end
end

# Detects if there is a file telling which files to check. Returns nil otherwise
def files_to_include
  return nil unless File.exist? ONLY_FILE_LIST

  includes = []
  File.foreach(ONLY_FILE_LIST).with_index do |line, _num|
    next unless line

    file = line.strip
    next if file.empty?

    includes.append file
  end

  includes
end

@includes = files_to_include

def includes_changes_to(type)
  return false if @includes.nil?

  @includes.each do |file|
    return true if file.end_with? type
  end

  false
end

def process_file?(filepath)
  if !@includes
    true
  else
    filepath = filepath.sub './', ''
    @includes.each do |file|
      return true if filepath.end_with? file
    end

    false
  end
end

def install_dotnet_tools
  puts 'Skipping restoring dotnet tools, hopefully they are up to date' unless @options[:restore_tools]

  TOOL_RESTORE_MUTEX.synchronize do
    if @tools_restored
      puts 'Tools already restored'
      return
    end

    @tools_restored = true

    info 'Restoring dotnet tools to make sure they are up to date'
    runOpen3Checked('dotnet', 'tool', 'restore')
  end
end

def handle_cs_file(path)
  errors = false

  original = File.read(path, encoding: 'utf-8')
  line_number = 0

  OUTPUT_MUTEX.synchronize do
    original.each_line do |line|
      line_number += 1

      if line.include? "\t"
        error "Line #{line_number} contains a tab"
        errors = true
      end

      if !OS.windows? && line.include?("\r\n")
        error "Line #{line_number} contains a windows style line ending (CR LF)"
        errors = true
      end

      # For some reason this reports 1 too high
      length = line.length - 1

      if length > MAX_LINE_LENGTH
        error "Line #{line_number} is too long. #{length} > #{MAX_LINE_LENGTH}"
        errors = true
      end
    end
  end

  errors
end

# Forwards the file handling to a specific handler function if
# something should be done with the file type
def handle_file(path)
  return false if file_type_skipped?(path) || !process_file?(path)

  if path =~ /\.cs$/
    handle_cs_file path
  else
    false
  end
end

# Run functions for the specific checks

def run_compile
  status = nil
  output = nil

  BUILD_MUTEX.synchronize do
    status, output = runOpen3CaptureOutput('dotnet', 'build', 'ThriveLauncher.sln',
                                           '/t:Clean,Build', '/warnaserror')
  end

  return if status.exitstatus.zero?

  OUTPUT_MUTEX.synchronize  do
    info 'Build output from dotnet:'
    puts output
    error "\nBuild generated warnings or errors."
  end
  exit 1
end

def run_files
  issues_found = false
  Find.find('.') do |path|
    next if skip_file? path

    begin
      if handle_file path
        OUTPUT_MUTEX.synchronize  do
          puts "Problems found in file (see above): #{path}"
          puts ''
        end
        issues_found = true
      end
    rescue StandardError => e
      OUTPUT_MUTEX.synchronize do
        puts "Failed to handle path: #{path}"
        puts "Error: #{e.message}"
      end
      raise e
    end
  end

  return unless issues_found

  OUTPUT_MUTEX.synchronize do
    error 'Code format issues detected'
  end
  exit 2
end

def skip_jetbrains?
  if @includes && !includes_changes_to('.cs')
    OUTPUT_MUTEX.synchronize do
      info 'No changes to be checked for .cs files'
    end
    return true
  end

  false
end

def run_inspect_code
  return if skip_jetbrains?

  install_dotnet_tools

  params = ['dotnet', 'tool', 'run', 'jb', 'inspectcode', 'ThriveLauncher.sln',
            '-o=inspect_results.xml', '--build', "--caches-home=#{JET_BRAINS_CACHE}"]

  params.append "--include=#{@includes.join(';')}" if @includes

  params.append "--exclude=#{EXCLUDE_INSPECT_FILES.join(';')}"

  BUILD_MUTEX.synchronize do
    runOpen3Checked(*params)
  end

  issues_found = false

  doc = Nokogiri::XML(File.open('inspect_results.xml'), &:norecover)

  issue_types = {}

  doc.xpath('//IssueType').each do |node|
    issue_types[node['Id']] = node
  end

  doc.xpath('//Issue').each do |issue|
    type = issue_types[issue['TypeId']]

    next if type['Severity'] == 'SUGGESTION'

    next if FORCE_IGNORED_INSPECTIONS.include? issue['TypeId']

    issues_found = true

    OUTPUT_MUTEX.synchronize do
      error "#{issue['File']}:#{issue['Line']} #{issue['Message']} type: #{issue['TypeId']}"
    end
  end

  return unless issues_found

  OUTPUT_MUTEX.synchronize do
    error 'Code inspection detected issues, see inspect_results.xml'
  end
  exit 2
end

def run_cleanup_code
  return if skip_jetbrains?

  install_dotnet_tools

  old_diff = runOpen3CaptureOutput 'git', '-c', 'core.safecrlf=false', 'diff', '--stat'

  params = ['dotnet', 'tool', 'run', 'jb', 'cleanupcode', 'ThriveLauncher.sln',
            '--profile=full_no_xml', "--caches-home=#{JET_BRAINS_CACHE}"]

  # TODO: we could probably split the includes into 4 groups to speed up things as the
  # cleanup tool doesn't run in parallel by default (so we could run 4 processes at once)
  params.append "--include=#{@includes.join(';')}" if @includes

  # Sometimes the code inspections fails completely and caches bad data if ran at the same time
  BUILD_MUTEX.synchronize do
    runOpen3Checked(*params)
  end

  new_diff = runOpen3CaptureOutput 'git', '-c', 'core.safecrlf=false', 'diff', '--stat'

  return if new_diff == old_diff

  OUTPUT_MUTEX.synchronize do
    error 'Code cleanup performed changes, please stage / check them before committing'
  end
  exit 2
end

run_check = proc { |check|
  if check == 'compile'
    run_compile
  elsif check == 'inspectcode'
    run_inspect_code
  elsif check == 'cleanupcode'
    run_cleanup_code
  else
    OUTPUT_MUTEX.synchronize do
      puts "Valid checks: #{VALID_CHECKS}"
      onError "Unknown check type: #{check}"
    end
  end
}

if @options[:parallel]
  threads = @options[:checks].map do |check|
    Thread.new do
      run_check.call check
    end
  end

  threads.map(&:join)
else
  @options[:checks].each do |check|
    run_check.call check
  end
end

success 'No code format issues found'
exit 0
