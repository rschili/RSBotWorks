#!/bin/bash

# Path to your .env file
ENV_FILE="../.env"

# Define image names
STOLL_IMAGE="stoll:latest"
WERNSTROM_IMAGE="wernstrom:latest"

# Check if the .env file exists
if [ ! -f "$ENV_FILE" ]; then
    echo ".env file not found!"
    exit 1
fi

# Export variables from the .env file
export $(grep -v '^#' $ENV_FILE | xargs)

# Check if the DOCKER_REGISTRY_URL has been set
if [ -z "$DOCKER_REGISTRY_URL" ]; then
    echo "DOCKER_REGISTRY_URL is not set!"
    exit 1
fi

# Build and push Stoll
docker buildx build -f Dockerfile.Stoll -t $STOLL_IMAGE .
if [ $? -ne 0 ]; then
    echo "Docker build for Stoll failed!"
    exit 1
fi
docker tag $STOLL_IMAGE $DOCKER_REGISTRY_URL/$STOLL_IMAGE
docker push $DOCKER_REGISTRY_URL/$STOLL_IMAGE
if [ $? -ne 0 ]; then
    echo "Docker push for Stoll failed!"
    exit 1
fi

# Build and push Wernstrom
docker buildx build -f Dockerfile.Wernstrom -t $WERNSTROM_IMAGE .
if [ $? -ne 0 ]; then
    echo "Docker build for Wernstrom failed!"
    exit 1
fi
docker tag $WERNSTROM_IMAGE $DOCKER_REGISTRY_URL/$WERNSTROM_IMAGE
docker push $DOCKER_REGISTRY_URL/$WERNSTROM_IMAGE
if [ $? -ne 0 ]; then
    echo "Docker push for Wernstrom failed!"
    exit 1
fi

echo "Both Docker images have been built and pushed successfully!"