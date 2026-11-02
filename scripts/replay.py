#!/usr/bin/env python3
"""Replay error queue using RabbitMQ Shovel publisher confirms; no destructive get/publish gap."""
import argparse
import os
import time
import urllib.parse
from demo import broker


def replay(queue):
    if not queue.endswith('_error'):
        raise ValueError('Only *_error queues may be replayed')
    destination = queue[:-6]
    src = broker('queues/%2F/' + urllib.parse.quote(queue, safe=''))
    if src['messages'] == 0:
        print('Error queue is empty.')
        return
    credentials = urllib.parse.quote(os.environ['RABBIT_USER'], safe='') + ':' + urllib.parse.quote(os.environ['RABBIT_PASSWORD'], safe='')
    uri = 'amqp://' + credentials + '@localhost/%2f'
    name = 'swiftride-replay-' + str(int(time.time()))
    broker('parameters/shovel/%2F/' + name, 'PUT', {'value': {'src-uri': uri, 'src-queue': queue,
        'dest-uri': uri, 'dest-queue': destination, 'ack-mode': 'on-confirm', 'src-prefetch-count': 10,
        'src-delete-after': 'queue-length'}})
    print(f'Started confirmed replay: {queue} to {destination}; original message IDs retained.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--queue', default='ride-completed_error')
    replay(parser.parse_args().queue)
