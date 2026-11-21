import boto3
import pytest
from moto import mock_aws

from helix_core.storage import Storage

BUCKET = "helix"


@pytest.fixture
def s3_storage():
    with mock_aws():
        client = boto3.client("s3", region_name="us-east-1")
        client.create_bucket(Bucket=BUCKET)
        yield Storage(client, BUCKET)
