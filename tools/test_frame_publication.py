"""Regression checks for publishing only complete captured frames."""
import unittest

from ring_reader import RingReader


def event(order, kind, sequence, page=0, sprite="main-10a2"):
    return {"order": order, "kind": kind, "frame_counter": sequence,
            "draw_page": page, "sprite_id": sprite}


class FramePublicationTests(unittest.TestCase):
    def setUp(self):
        self.reader = RingReader.__new__(RingReader)
        self.reader.last_frame = None

    def publish(self, events):
        self.reader.snapshot = lambda: {"total_events": len(events), "events": events}
        return self.reader.frame()

    def test_partial_next_frame_keeps_previous_complete_frame(self):
        frame = self.publish([event(0, "begin", 7), event(1, "draw", 7),
                              event(2, "end", 7), event(3, "begin", 8), event(4, "draw", 8)])
        self.assertEqual(frame["frame_key"], [7, 2])
        self.assertEqual([e["order"] for e in frame["sprites"]], [1])

    def test_ignores_other_pages_inside_a_complete_pass(self):
        frame = self.publish([event(0, "begin", 7, 8192), event(1, "draw", 7, 0),
                              event(2, "draw", 7, 8192), event(3, "end", 7, 8192)])
        self.assertEqual([e["order"] for e in frame["sprites"]], [2])

    def test_truncated_capture_cannot_replace_the_last_complete_frame(self):
        valid = self.publish([event(0, "begin", 65535), event(1, "draw", 65535), event(2, "end", 65535)])
        truncated = self.publish([event(8, "draw", 0), event(9, "end", 0)])
        self.assertEqual(truncated["frame_key"], valid["frame_key"])
        self.assertEqual(truncated["sprites"], valid["sprites"])


if __name__ == "__main__":
    unittest.main()
