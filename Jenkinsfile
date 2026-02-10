pipeline {
  agent none
  stages {
    stage('Build and Deploy') {
      parallel {
        stage('API Pipeline') {
          stages {
            stage('Build API Docker Image') {
              agent {
                kubernetes {
                  cloud 'Local k8s'
                  yamlFile 'deploy/pod.yaml'
                  nodeSelector 'kubernetes.io/hostname=bethany'
                }
              }
              steps {
                container('dind') {
                  withCredentials([usernamePassword(credentialsId: 'jenkins-bf1942-stats-dockerhub-pat', usernameVariable: 'DOCKER_USERNAME', passwordVariable: 'DOCKER_PASSWORD')]) {
                    sh '''
                      # Login to Docker Hub
                      echo "$DOCKER_PASSWORD" | docker login -u "$DOCKER_USERNAME" --password-stdin

                      # Setup Docker buildx for cross-platform builds with DinD optimizations
                      docker buildx create --name multiarch-builder --driver docker-container --use || true
                      docker buildx use multiarch-builder

                      # Build and push ARM64 image for API (cross-compiled natively on amd64)
                      DOCKER_BUILDKIT=1 docker buildx build -f deploy/Dockerfile . \
                        --platform linux/arm64 \
                        --build-arg PROJECT_PATH=api \
                        --build-arg PROJECT_NAME=api \
                        --build-arg BUILDKIT_PROGRESS=plain \
                        --push \
                        -t dylanmunyard/bf42-stats:latest
                    '''
                  }
                }
              }
            }
            stage('Deploy API') {
              agent {
                kubernetes {
                  cloud 'Local k8s'
                  yamlFile 'deploy/pod.yaml'
                  nodeSelector 'kubernetes.io/hostname=bethany'
                }
              }
              steps {
                container('kubectl') {
                  withCredentials([
                    file(credentialsId: 'bf42-stats-k3s-kubeconfig', variable: 'KUBECONFIG_FILE'),
                    string(credentialsId: 'bf42-stats-secrets-jwt-private-key', variable: 'JWT_PRIVATE_KEY'),
                    string(credentialsId: 'bf42-stats-secrets-refresh-token-secret', variable: 'REFRESH_TOKEN_SECRET')
                  ]) {
                    sh '''
                      set -euo pipefail
                      export KUBECONFIG="$KUBECONFIG_FILE"
                      TMPDIR=$(mktemp -d)
                      trap 'rm -rf "$TMPDIR"' EXIT
                      printf "%s" "$JWT_PRIVATE_KEY" > "$TMPDIR/jwt-private.pem"
                      kubectl -n bf42-stats create secret generic bf42-stats-secrets \
                        --from-file=jwt-private-key="$TMPDIR/jwt-private.pem" \
                        --from-literal=refresh-token-secret="$REFRESH_TOKEN_SECRET" \
                        --dry-run=client -o yaml | kubectl apply -f -
                      kubectl -n bf42-stats rollout restart deployment/bf42-stats
                    '''
                  }
                }
              }
            }
          }
        }
        stage('Notifications Pipeline') {
          stages {
            stage('Build Notifications Docker Image') {
              agent {
                kubernetes {
                  cloud 'Local k8s'
                  yamlFile 'deploy/pod.yaml'
                  nodeSelector 'kubernetes.io/hostname=bethany'
                }
              }
              steps {
                container('dind') {
                  withCredentials([usernamePassword(credentialsId: 'jenkins-bf1942-stats-dockerhub-pat', usernameVariable: 'DOCKER_USERNAME', passwordVariable: 'DOCKER_PASSWORD')]) {
                    sh '''
                      # Login to Docker Hub
                      echo "$DOCKER_PASSWORD" | docker login -u "$DOCKER_USERNAME" --password-stdin

                      # Setup Docker buildx for cross-platform builds with DinD optimizations
                      docker buildx create --name multiarch-builder-notif --driver docker-container --use || true
                      docker buildx use multiarch-builder-notif

                      # Build and push ARM64 image for Notifications (cross-compiled natively on amd64)
                      DOCKER_BUILDKIT=1 docker buildx build -f deploy/Dockerfile . \
                        --platform linux/arm64 \
                        --build-arg PROJECT_PATH=notifications \
                        --build-arg PROJECT_NAME=notifications \
                        --build-arg BUILDKIT_PROGRESS=plain \
                        --push \
                        -t dylanmunyard/bf42-notifications:latest
                    '''
                  }
                }
              }
            }
            stage('Deploy Notifications') {
              agent {
                kubernetes {
                  cloud 'Local k8s'
                  yamlFile 'deploy/pod.yaml'
                  nodeSelector 'kubernetes.io/hostname=bethany'
                }
              }
              steps {
                container('kubectl') {
                  withCredentials([
                    file(credentialsId: 'bf42-stats-k3s-kubeconfig', variable: 'KUBECONFIG_FILE')
                  ]) {
                    sh '''
                      set -euo pipefail
                      export KUBECONFIG="$KUBECONFIG_FILE"
                      kubectl -n bf42-stats rollout restart deployment/bf42-notifications
                    '''
                  }
                }
              }
            }
          }
        }
      }
    }
  }
}
