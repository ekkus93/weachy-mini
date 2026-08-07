#ifndef REACHY_LLAMA_INTERNAL_HPP
#define REACHY_LLAMA_INTERNAL_HPP

#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <mutex>
#include <string>
#include <utility>

namespace reachy_llama_internal
{
struct StreamChunk
{
    uint64_t sequence;
    std::string text;
};

class BoundedStreamQueue
{
public:
    explicit BoundedStreamQueue(std::size_t capacity)
        : capacity_(capacity)
    {
    }

    BoundedStreamQueue(const BoundedStreamQueue &) = delete;
    BoundedStreamQueue & operator=(const BoundedStreamQueue &) = delete;

    [[nodiscard]] bool Push(uint64_t sequence, std::string text)
    {
        std::unique_lock<std::mutex> lock(mutex_);
        changed_.wait(lock, [this] { return cancelled_ || chunks_.size() < capacity_; });
        if (cancelled_)
        {
            return false;
        }
        chunks_.push_back(StreamChunk{sequence, std::move(text)});
        changed_.notify_all();
        return true;
    }

    [[nodiscard]] bool Peek(StreamChunk & chunk) const
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (chunks_.empty())
        {
            return false;
        }
        chunk = chunks_.front();
        return true;
    }

    [[nodiscard]] bool Pop(StreamChunk & chunk)
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (chunks_.empty())
        {
            return false;
        }
        chunk = std::move(chunks_.front());
        chunks_.pop_front();
        changed_.notify_all();
        return true;
    }

    void Cancel()
    {
        std::lock_guard<std::mutex> lock(mutex_);
        cancelled_ = true;
        changed_.notify_all();
    }

    [[nodiscard]] bool IsCancelled() const
    {
        std::lock_guard<std::mutex> lock(mutex_);
        return cancelled_;
    }

    [[nodiscard]] std::size_t Size() const
    {
        std::lock_guard<std::mutex> lock(mutex_);
        return chunks_.size();
    }

private:
    const std::size_t capacity_;
    mutable std::mutex mutex_;
    std::condition_variable changed_;
    std::deque<StreamChunk> chunks_;
    bool cancelled_{false};
};
} // namespace reachy_llama_internal

#endif
