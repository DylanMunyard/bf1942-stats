pipeline {
  stages {
    stage('Build Images') {
      agent {
        kubernetes {
          cloud 'Local k8s'
          yamlFile 'deploy/pod.yaml'
          nodeSelector 'k3s.io/hostname=bethany'
        }
      }
      parallel {
        stage('Build API Docker Image') {
          steps {
            container('dind') {
              withCredentials([usernamePassword(credentialsId: 'jenkins-bf1942-stats-dockerhub-pat', usernameVariable: 'DOCKER_USERNAME', passwordVariable: 'DOCKER_PASSWORD')]) {
                sh '''
                  # Login to Docker Hub
                  echo "$DOCKER_PASSWORD" | docker login -u "$DOCKER_USERNAME" --password-stdin
                  
                  # Setup Docker buildx for cross-platform builds
                  docker buildx create --name multiarch-builder --use --bootstrap || true
                  docker buildx use multiarch-builder
                  
                  # Build and push ARM64 image for API
                  docker buildx build -f deploy/Dockerfile . \
                    --platform linux/arm64 \
                    --build-arg PROJECT_PATH=junie-des-1942stats \
                    --build-arg PROJECT_NAME=junie-des-1942stats \
                    -t dylanmunyard/bf42-stats:latest \
                    --push
                '''
              }
            }
          }
        }
        stage('Build Notifications Docker Image') {
          steps {
            container('dind') {
              withCredentials([usernamePassword(credentialsId: 'jenkins-bf1942-stats-dockerhub-pat', usernameVariable: 'DOCKER_USERNAME', passwordVariable: 'DOCKER_PASSWORD')]) {
                sh '''
                  # Login to Docker Hub
                  echo "$DOCKER_PASSWORD" | docker login -u "$DOCKER_USERNAME" --password-stdin
                  
                  # Setup Docker buildx for cross-platform builds
                  docker buildx create --name multiarch-builder --use --bootstrap || true
                  docker buildx use multiarch-builder
                  
                  # Build and push ARM64 image for Notifications
                  docker buildx build -f deploy/Dockerfile . \
                    --platform linux/arm64 \
                    --build-arg PROJECT_PATH=junie-des-1942stats.Notifications \
                    --build-arg PROJECT_NAME=junie-des-1942stats.Notifications \
                    -t dylanmunyard/bf42-notifications:latest \
                    --push
                '''
              }
            }
          }
        }
      }
    }
    stage('Deploy to AKS') {
      agent {
        kubernetes {
          cloud 'AKS'
          yamlFile 'deploy/pod.yaml'
        }
      }
      parallel {
        stage('Deploy API') {
          steps {
            container('kubectl') {
              withKubeConfig([namespace: "bf42-stats"]) {
                 withCredentials([
                   string(credentialsId: 'bf42-stats-secrets-jwt-private-key', variable: 'JWT_PRIVATE_KEY'),
                   string(credentialsId: 'bf42-stats-secrets-refresh-token-secret', variable: 'REFRESH_TOKEN_SECRET')
                 ]) {
                   sh '''
                     set -euo pipefail
                     TMPDIR=$(mktemp -d)
                     trap 'rm -rf "$TMPDIR"' EXIT
                     printf "%s" "$JWT_PRIVATE_KEY" > "$TMPDIR/jwt-private.pem"
                     # Create or update the bf42-stats-secrets secret with both keys
                     kubectl create secret generic bf42-stats-secrets \
                       --from-file=jwt-private-key="$TMPDIR/jwt-private.pem" \
                       --from-literal=refresh-token-secret="$REFRESH_TOKEN_SECRET" \
                       --dry-run=client -o yaml | kubectl apply -f -
                   '''
                 }
                sh 'kubectl rollout restart deployment/bf42-stats'
              }
            }
          }
        }
        stage('Deploy Notifications') {
          steps {
            container('kubectl') {
              withKubeConfig([namespace: "bf42-stats"]) {
                sh 'kubectl rollout restart deployment/bf42-notifications'
              }
            }
          }
        }
      }
    }
  }
}