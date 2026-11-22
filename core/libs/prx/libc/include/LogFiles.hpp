#pragma once
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include "GameSerial.hpp"
#include <string>
#include <system_error>

// File management shared by runtimes; no operating-system headers or APIs.
namespace RuntimeLogFiles {
inline std::string Timestamp() {
    using namespace std::chrono;
    const auto now = floor<milliseconds>(system_clock::now());
    const auto day = floor<days>(now);
    const year_month_day date{day};
    const hh_mm_ss clock{now - day};
    char stamp[40];
    std::snprintf(stamp, sizeof(stamp), "%04d%02u%02uT%02lld%02lld%02lld%03lldZ",
        int(date.year()), unsigned(date.month()), unsigned(date.day()),
        static_cast<long long>(clock.hours().count()), static_cast<long long>(clock.minutes().count()),
        static_cast<long long>(clock.seconds().count()), static_cast<long long>(clock.subseconds().count()));
    return stamp;
}

// FIX-RT-026: an archived log keeps its extension: stdout-20261122T172045123Z.log, not
// stdout.log.20261122T172045123Z. GitHub (and other sites) refused to attach a file whose
// extension is a timestamp, so players could not upload the log of the session that failed.
// `tag` marks a log moved from the package root ("-legacy"); `i` resolves a same-millisecond
// collision ("-1", "-2", ...).
inline std::filesystem::path ArchivePath(const std::filesystem::path& current, const std::string& stamp,
                                         const std::string& tag, unsigned i) {
    auto name = current.stem().string() + "-" + stamp + tag + (i ? "-" + std::to_string(i) : "");
    return current.parent_path() / (name + current.extension().string());
}

inline bool Archive(const std::filesystem::path& source, const std::filesystem::path& current,
                    const std::string& tag = {}) {
    std::error_code error;
    if (!std::filesystem::exists(source, error)) return !error;
    const auto stamp = Timestamp();
    for (unsigned i = 0; i < 10000; ++i) {
        const auto target = ArchivePath(current, stamp, tag, i);
        // Creating a new link cannot overwrite an existing archive, even if another
        // process archives at the same instant. Copy without overwrite on other volumes.
        std::filesystem::create_hard_link(source, target, error);
        if (error == std::errc::file_exists) continue;
        if (error) {
            error.clear();
            std::filesystem::copy_file(source, target, std::filesystem::copy_options::none, error);
            if (error == std::errc::file_exists) continue;
            if (error) return false;
        }
        std::filesystem::remove(source, error);
        return !error;
    }
    return false;
}

inline std::filesystem::path Prepare(const std::filesystem::path& root, const std::filesystem::path& name, bool runtimeLaunch = false) {
    std::error_code error;
    // logs\<serial>: each game's logs in its own folder (GameSerial.hpp, GAME-004).
    const auto directory = root / "logs" / GameSerial::GameSerial();
    std::filesystem::create_directories(directory, error);
    if (error) return {};
    const auto current = directory / name;
    if (runtimeLaunch) {
        if (const char* prepared = std::getenv("RYGT_LOG_DIRECTORY")) {
            if (std::filesystem::equivalent(directory, prepared, error) && !error)
                return current; // The launcher has already rotated and opened this session.
            error.clear();
        }
    }
    if (!Archive(root / name, current, "-legacy") || !Archive(current, current)) return {};
    return current;
}
}
